using OLED_Sleeper.Core.Interfaces;
using OLED_Sleeper.Features.MonitorInformation.Models;
using OLED_Sleeper.Features.MonitorInformation.Services.Interfaces;
using OLED_Sleeper.Features.MonitorState.Commands;
using OLED_Sleeper.Features.MonitorState.Services.Interfaces;
using Serilog;
using System.Timers;
using Timer = System.Timers.Timer;

namespace OLED_Sleeper.Features.MonitorState.Services
{
    /// <summary>
    /// Monitors the set of connected displays and dispatches synchronization commands when
    /// changes are detected.
    /// </summary>
    /// <remarks>
    /// Cheap basic poll runs every <c>pollIntervalMs</c> and detects connect/disconnect by
    /// device-name set. To handle the cold-boot case where DDC/CI reports <c>false</c> for a
    /// monitor that later starts reporting <c>true</c> once drivers settle, the watcher also
    /// schedules a single deferred deep re-validation <see cref="BootDeepRevalidateMs"/>
    /// after the initial sync. A periodic deep poll is intentionally avoided: in the field
    /// it caused phantom virtual-display flapping (DISPLAY10/11/12...) to churn DDC
    /// enumeration every 30 s, restarting idle detection mid-cycle and freezing the app.
    /// Re-syncs are also rate-limited to <see cref="MinResyncIntervalMs"/> apart so a
    /// flapping phantom monitor cannot stop/start idle detection in tight loops.
    /// </remarks>
    public class MonitorStateWatcher : IMonitorStateWatcher
    {
        #region Fields

        // One-shot deferred deep re-validate runs ~90s after initial sync. Catches the
        // cold-boot DDC/CI=false→true flip without any periodic churn.
        private const double BootDeepRevalidateMs = 90_000;

        // Floor between consecutive resyncs. Phantom virtual displays flapping faster than
        // this won't restart idle detection.
        private const double MinResyncIntervalMs = 30_000;

        private readonly IMonitorInfoManager _monitorInfoManager;
        private readonly IMediator _mediator;
        private readonly Timer _pollTimer;
        private readonly Timer _bootRevalidateTimer;
        private readonly object _lock = new();
        private IReadOnlyList<MonitorInfo> _lastKnownMonitors = Array.Empty<MonitorInfo>();
        private DateTime _lastResyncUtc = DateTime.MinValue;

        #endregion Fields

        #region Constructor

        public MonitorStateWatcher(IMonitorInfoManager monitorInfoManager, IMediator mediator, double pollIntervalMs = 2000)
        {
            _monitorInfoManager = monitorInfoManager;
            _mediator = mediator;

            _pollTimer = new Timer(pollIntervalMs) { AutoReset = true };
            _pollTimer.Elapsed += PollTimerElapsed;

            _bootRevalidateTimer = new Timer(BootDeepRevalidateMs) { AutoReset = false };
            _bootRevalidateTimer.Elapsed += BootRevalidateElapsed;
        }

        #endregion Constructor

        #region Public Methods

        public void Start()
        {
            lock (_lock)
            {
                if (!_pollTimer.Enabled)
                {
                    RetrieveInitialMonitorList();
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _pollTimer.Stop();
                _bootRevalidateTimer.Stop();
            }
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
            _bootRevalidateTimer?.Dispose();
        }

        #endregion Public Methods

        #region Private Methods

        private void RetrieveInitialMonitorList()
        {
            EventHandler<IReadOnlyList<MonitorInfo>> handler = null!;
            handler = (sender, monitors) =>
            {
                _monitorInfoManager.MonitorListReady -= handler;
                _lastKnownMonitors = monitors;
                _lastResyncUtc = DateTime.UtcNow;
                _mediator.SendAsync(new SynchronizeMonitorStateCommand([], _lastKnownMonitors));
                _pollTimer.Start();

                // Only schedule the boot re-validate when we are actually still in the
                // boot-settle window. After that, drivers are stable and any later DDC/CI
                // change will arrive via PowerEventMonitor (resume/unlock) or a real
                // device-name change picked up by the basic poll.
                if (Environment.TickCount64 < 120_000)
                {
                    _bootRevalidateTimer.Start();
                }
            };
            _monitorInfoManager.MonitorListReady += handler;
            _monitorInfoManager.GetCurrentMonitorsAsync();
        }

        private void PollTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                var currentMonitors = _monitorInfoManager.GetLatestMonitorsBasicInfo();
                if (AreBasicMonitorListsEqual(_lastKnownMonitors, currentMonitors)) return;

                if ((DateTime.UtcNow - _lastResyncUtc).TotalMilliseconds < MinResyncIntervalMs)
                {
                    // Phantom display flapping. Skip — the next poll inside the window will
                    // simply find the device-name set unchanged again.
                    return;
                }

                EnrichMonitorInfoList(currentMonitors);
                DispatchResync(currentMonitors, "basic poll detected device-name change");
            }
        }

        private void BootRevalidateElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                var currentMonitors = _monitorInfoManager.GetLatestMonitorsBasicInfo();
                EnrichMonitorInfoList(currentMonitors);

                if (AreEnrichedMonitorListsEqual(_lastKnownMonitors, currentMonitors))
                {
                    Log.Debug("Boot deep re-validate: no capability changes.");
                    return;
                }

                DispatchResync(currentMonitors, "boot deep re-validate found DDC/CI or HardwareId change");
            }
        }

        private void DispatchResync(List<MonitorInfo> currentMonitors, string reason)
        {
            Log.Information("Monitor state changed ({Reason}). Re-syncing {Count} monitors.", reason, currentMonitors.Count);

            // Push the freshly enriched list into the manager's cache so any consumer
            // calling GetCurrentMonitorsAsync afterwards (workspace UI, blackout overlay
            // placement, dim service) sees the updated capabilities instead of the stale
            // boot-time snapshot.
            _monitorInfoManager.UpdateCachedMonitors(currentMonitors);

            var oldMonitors = _lastKnownMonitors;
            _lastKnownMonitors = currentMonitors;
            _lastResyncUtc = DateTime.UtcNow;
            _mediator.SendAsync(new SynchronizeMonitorStateCommand(oldMonitors, currentMonitors));
        }

        /// <summary>
        /// Cheap comparison — just device-name set. Used to detect monitor connect/disconnect
        /// without paying the cost of DDC/CI enumeration.
        /// </summary>
        internal static bool AreBasicMonitorListsEqual(IReadOnlyList<MonitorInfo>? a, IReadOnlyList<MonitorInfo>? b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            var aNames = new HashSet<string>(a.Select(m => m.DeviceName).OfType<string>());
            var bNames = new HashSet<string>(b.Select(m => m.DeviceName).OfType<string>());
            return aNames.SetEquals(bNames);
        }

        /// <summary>
        /// Full comparison including HardwareId and DDC/CI support. Catches the case where
        /// the device-name set is unchanged but the underlying capabilities flipped (e.g.
        /// DDC/CI reported <c>false</c> at boot then <c>true</c> after the driver settles).
        /// </summary>
        internal static bool AreEnrichedMonitorListsEqual(IReadOnlyList<MonitorInfo>? a, IReadOnlyList<MonitorInfo>? b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            string Key(MonitorInfo m) =>
                $"{m.DeviceName ?? string.Empty}|{m.HardwareId ?? string.Empty}|{(m.IsDdcCiSupported ? 1 : 0)}";

            var aKeys = new HashSet<string>(a.Select(Key));
            var bKeys = new HashSet<string>(b.Select(Key));
            return aKeys.SetEquals(bKeys);
        }

        private void EnrichMonitorInfoList(List<MonitorInfo> monitors)
        {
            _monitorInfoManager.EnrichMonitorInfoList(monitors);
        }

        #endregion Private Methods
    }
}
