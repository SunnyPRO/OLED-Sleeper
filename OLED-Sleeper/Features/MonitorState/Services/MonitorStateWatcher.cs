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
    /// Monitors the set of connected displays and dispatches synchronization commands when changes are detected.
    /// </summary>
    /// <remarks>
    /// Performs two kinds of polls. A cheap basic poll runs every <c>pollIntervalMs</c> and
    /// detects connect/disconnect by device-name set. A periodic deep poll runs at
    /// <see cref="DeepPollIntervalMs"/> and re-enriches the cached info so that DDC/CI
    /// support flips and hardware-id changes — which the basic poll cannot see — also
    /// trigger a state synchronization. Without the deep poll, monitors that boot with
    /// DDC/CI reporting <c>false</c> never become managed even after the driver settles
    /// and starts reporting <c>true</c>; observed in the field on cold boot.
    /// </remarks>
    public class MonitorStateWatcher : IMonitorStateWatcher
    {
        #region Fields

        // Re-enrich (DDC/CI + HardwareId) every 30s to catch boot-time flips that the
        // device-name poll cannot see.
        private const double DeepPollIntervalMs = 30_000;

        private readonly IMonitorInfoManager _monitorInfoManager;
        private readonly IMediator _mediator;
        private readonly Timer _pollTimer;
        private readonly object _lock = new();
        private IReadOnlyList<MonitorInfo> _lastKnownMonitors = Array.Empty<MonitorInfo>();
        private DateTime _lastDeepPollUtc = DateTime.MinValue;

        #endregion Fields

        #region Constructor

        public MonitorStateWatcher(IMonitorInfoManager monitorInfoManager, IMediator mediator, double pollIntervalMs = 2000)
        {
            _monitorInfoManager = monitorInfoManager;
            _mediator = mediator;
            _pollTimer = new Timer(pollIntervalMs) { AutoReset = true };
            _pollTimer.Elapsed += PollTimerElapsed;
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
            }
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
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
                _lastDeepPollUtc = DateTime.UtcNow;
                _mediator.SendAsync(new SynchronizeMonitorStateCommand([], _lastKnownMonitors));
                _pollTimer.Start();
            };
            _monitorInfoManager.MonitorListReady += handler;
            _monitorInfoManager.GetCurrentMonitorsAsync();
        }

        private void PollTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                var currentMonitors = _monitorInfoManager.GetLatestMonitorsBasicInfo();
                bool basicChange = !AreBasicMonitorListsEqual(_lastKnownMonitors, currentMonitors);

                bool deepDue = (DateTime.UtcNow - _lastDeepPollUtc).TotalMilliseconds >= DeepPollIntervalMs;
                if (basicChange || deepDue)
                {
                    EnrichMonitorInfoList(currentMonitors);
                    _lastDeepPollUtc = DateTime.UtcNow;

                    if (basicChange || !AreEnrichedMonitorListsEqual(_lastKnownMonitors, currentMonitors))
                    {
                        Log.Information(
                            "Monitor state changed (basicChange={BasicChange}, deepPoll={DeepDue}). Re-syncing {Count} monitors.",
                            basicChange, deepDue, currentMonitors.Count);

                        // Push the freshly enriched list into the manager's cache so any consumer
                        // that calls GetCurrentMonitorsAsync afterwards (workspace UI, blackout
                        // overlay placement, dim service) sees the updated capabilities instead
                        // of the stale boot-time snapshot.
                        _monitorInfoManager.UpdateCachedMonitors(currentMonitors);

                        var oldMonitors = _lastKnownMonitors;
                        _lastKnownMonitors = currentMonitors;
                        _mediator.SendAsync(new SynchronizeMonitorStateCommand(oldMonitors, currentMonitors));
                    }
                }
            }
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
