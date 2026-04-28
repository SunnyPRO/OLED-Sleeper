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

        private const double BootDeepRevalidateMs = 90_000;
        private const double MinResyncIntervalMs = 30_000;

        private readonly IMonitorInfoManager _monitorInfoManager;
        private readonly IMediator _mediator;
        private readonly Timer _pollTimer;
        private readonly Timer _bootRevalidateTimer;
        private readonly object _lock = new();

        // Touched only under _lock, except _isPollRunning which is Interlocked-guarded.
        private IReadOnlyList<MonitorInfo> _lastKnownMonitors = Array.Empty<MonitorInfo>();
        private DateTime _lastResyncUtc = DateTime.MinValue;
        // DeviceNames that have been observed with an empty HardwareId after enrichment —
        // i.e. phantom virtual displays (iGPU bridges, RDP, virtual cameras). Cached so the
        // basic poll can ignore connect/disconnect of these names without re-running DDC
        // enumeration every time they flap.
        private HashSet<string> _knownPhantomDeviceNames = new();
        private int _isPollRunning;
        private bool _isStopped;

        #endregion Fields

        public MonitorStateWatcher(IMonitorInfoManager monitorInfoManager, IMediator mediator, double pollIntervalMs = 2000)
        {
            _monitorInfoManager = monitorInfoManager;
            _mediator = mediator;

            _pollTimer = new Timer(pollIntervalMs) { AutoReset = true };
            _pollTimer.Elapsed += PollTimerElapsed;

            _bootRevalidateTimer = new Timer(BootDeepRevalidateMs) { AutoReset = false };
            _bootRevalidateTimer.Elapsed += BootRevalidateElapsed;
        }

        #region Public Methods

        public void Start()
        {
            lock (_lock)
            {
                _isStopped = false;
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
                _isStopped = true;
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
                lock (_lock)
                {
                    if (_isStopped) return;
                    var manageable = FilterManageableMonitors(monitors);
                    UpdatePhantomCache(monitors, manageable);
                    _lastKnownMonitors = manageable;
                    _lastResyncUtc = DateTime.UtcNow;
                    _mediator.SendAsync(new SynchronizeMonitorStateCommand([], _lastKnownMonitors));
                    _pollTimer.Start();

                    if (Environment.TickCount64 < 120_000)
                    {
                        _bootRevalidateTimer.Start();
                    }
                }
            };
            _monitorInfoManager.MonitorListReady += handler;
            _monitorInfoManager.GetCurrentMonitorsAsync();
        }

        private void PollTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            // Reject re-entry. A poll that overruns its interval (DDC enumeration takes
            // ~3s/monitor) must not stack on the threadpool — that pile-up was part of the
            // freeze symptom.
            if (Interlocked.Exchange(ref _isPollRunning, 1) == 1) return;
            try
            {
                RunBasicPoll();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MonitorStateWatcher basic poll failed.");
            }
            finally
            {
                Interlocked.Exchange(ref _isPollRunning, 0);
            }
        }

        private void RunBasicPoll()
        {
            // Take a quick snapshot under the lock, then do slow work outside.
            IReadOnlyList<MonitorInfo> lastKnownSnapshot;
            DateTime lastResyncSnapshot;
            HashSet<string> phantomSnapshot;
            lock (_lock)
            {
                if (_isStopped) return;
                lastKnownSnapshot = _lastKnownMonitors;
                lastResyncSnapshot = _lastResyncUtc;
                phantomSnapshot = new HashSet<string>(_knownPhantomDeviceNames);
            }

            var basicCurrent = _monitorInfoManager.GetLatestMonitorsBasicInfo();
            // Drop names already classified as phantoms so their flapping doesn't trigger
            // an enrichment pass. They'll be re-classified on the boot revalidate or the
            // next genuine non-phantom change.
            var basicCurrentManageable = basicCurrent.Where(m => m.DeviceName != null && !phantomSnapshot.Contains(m.DeviceName)).ToList();
            if (AreBasicMonitorListsEqual(lastKnownSnapshot, basicCurrentManageable)) return;

            if ((DateTime.UtcNow - lastResyncSnapshot).TotalMilliseconds < MinResyncIntervalMs)
            {
                // Even after phantom suppression we got a genuine flap inside the cooldown.
                // Skip — the next poll outside the window will catch it.
                return;
            }

            // DDC enrichment is slow (seconds per monitor). Run it WITHOUT the lock so
            // Stop()/Start() and the boot revalidate timer can proceed.
            EnrichMonitorInfoList(basicCurrent);
            var manageable = FilterManageableMonitors(basicCurrent);

            // Update phantom cache while we have fresh enrichment data.
            lock (_lock)
            {
                if (_isStopped) return;
                UpdatePhantomCache(basicCurrent, manageable);
            }

            CommitResync(manageable, lastKnownSnapshot, "basic poll detected device-name change");
        }

        private void BootRevalidateElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                IReadOnlyList<MonitorInfo> lastKnownSnapshot;
                lock (_lock)
                {
                    if (_isStopped) return;
                    lastKnownSnapshot = _lastKnownMonitors;
                }

                var current = _monitorInfoManager.GetLatestMonitorsBasicInfo();
                EnrichMonitorInfoList(current);
                var manageable = FilterManageableMonitors(current);

                lock (_lock)
                {
                    if (_isStopped) return;
                    UpdatePhantomCache(current, manageable);
                }

                if (AreEnrichedMonitorListsEqual(lastKnownSnapshot, manageable))
                {
                    Log.Debug("Boot deep re-validate: no capability changes.");
                    return;
                }

                CommitResync(manageable, lastKnownSnapshot, "boot deep re-validate found DDC/CI or HardwareId change");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MonitorStateWatcher boot revalidate failed.");
            }
        }

        private void CommitResync(List<MonitorInfo> manageable, IReadOnlyList<MonitorInfo> oldSnapshot, string reason)
        {
            // Re-check stop + concurrent updates under the lock before publishing.
            lock (_lock)
            {
                if (_isStopped) return;
                if (!ReferenceEquals(_lastKnownMonitors, oldSnapshot))
                {
                    // Another path (e.g. the boot revalidate) raced ahead while we were
                    // doing the slow DDC enumeration. Drop our (now stale) result.
                    return;
                }

                Log.Information("Monitor state changed ({Reason}). Re-syncing {Count} monitors.", reason, manageable.Count);

                _monitorInfoManager.UpdateCachedMonitors(manageable);
                _lastKnownMonitors = manageable;
                _lastResyncUtc = DateTime.UtcNow;
                _mediator.SendAsync(new SynchronizeMonitorStateCommand(oldSnapshot, manageable));
            }
        }

        /// <summary>
        /// Drops monitors that cannot be managed downstream. Phantom virtual displays from
        /// iGPU bridges, RDP, and virtual cameras frequently appear in
        /// <see cref="NativeMethods.EnumDisplayMonitors"/> with empty hardware IDs; the idle
        /// detection service joins settings on HardwareId so they could never become active
        /// anyway, and including them in comparisons just creates churn.
        /// </summary>
        /// <remarks>
        /// Must be called only after <c>EnrichMonitorInfoList</c> — pre-enrichment all
        /// HardwareIds are null and the filter would drop everything.
        /// </remarks>
        internal static List<MonitorInfo> FilterManageableMonitors(IReadOnlyList<MonitorInfo> monitors)
        {
            var result = new List<MonitorInfo>(monitors.Count);
            foreach (var m in monitors)
            {
                if (string.IsNullOrEmpty(m.DeviceName)) continue;
                if (string.IsNullOrEmpty(m.HardwareId)) continue;
                result.Add(m);
            }
            return result;
        }

        /// <summary>
        /// Synchronizes the phantom cache with the latest enrichment snapshot. DeviceNames
        /// missing a HardwareId are added; DeviceNames that have come back as manageable
        /// are removed so a name that flips from phantom→real (e.g. RDP reconnect, dock
        /// hot-plug, driver settle) is no longer suppressed by the basic poll. Without the
        /// removal step the cache would be monotonic and a real monitor reusing a
        /// previously-phantom name would be invisible to the basic poll, causing both
        /// missed detections and repeated resyncs after the next enrichment.
        /// </summary>
        private void UpdatePhantomCache(IReadOnlyList<MonitorInfo> enriched, List<MonitorInfo> manageable)
        {
            var manageableNames = new HashSet<string>(manageable.Select(m => m.DeviceName!).OfType<string>());
            // Names now manageable can no longer be phantoms.
            _knownPhantomDeviceNames.ExceptWith(manageableNames);
            foreach (var m in enriched)
            {
                if (m.DeviceName == null) continue;
                if (manageableNames.Contains(m.DeviceName)) continue;
                _knownPhantomDeviceNames.Add(m.DeviceName);
            }
        }

        /// <summary>
        /// Cheap comparison — just device-name set.
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
        /// Full comparison including HardwareId and DDC/CI support.
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
