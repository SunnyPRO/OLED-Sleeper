using OLED_Sleeper.Features.MonitorInformation.Models;
using OLED_Sleeper.Features.MonitorInformation.Services.Interfaces;
using Serilog;

namespace OLED_Sleeper.Features.MonitorInformation.Services
{
    /// <summary>
    /// Manages monitor information, including caching and enrichment with DDC/CI support and hardware IDs.
    /// Publishes an event when the monitor list is ready after async retrieval.
    /// </summary>
    public class MonitorInfoManager : IMonitorInfoManager
    {
        #region Fields

        private readonly IMonitorInfoProvider _monitorInfoProvider;
        private List<MonitorInfo>? _cachedMonitors;
        private readonly object _lock = new object();
        private Task<List<MonitorInfo>>? _refreshTask;

        #endregion Fields

        #region Events

        /// <summary>
        /// Raised when the monitor list has been retrieved and enriched.
        /// </summary>
        public event EventHandler<IReadOnlyList<MonitorInfo>>? MonitorListReady;

        #endregion Events

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitorInfoManager"/> class.
        /// </summary>
        /// <param name="monitorInfoProvider">The monitor info provider dependency.</param>
        public MonitorInfoManager(IMonitorInfoProvider monitorInfoProvider)
        {
            _monitorInfoProvider = monitorInfoProvider;
        }

        #endregion Constructor

        #region Public Methods

        /// <summary>
        /// Begins asynchronous retrieval and enrichment of the monitor list.
        /// Ensures only one refresh runs at a time. Subscribers will be notified via <see cref="MonitorListReady"/> when the list is available.
        /// If the cache is already populated, the event is raised immediately.
        /// </summary>
        public void GetCurrentMonitorsAsync()
        {
            IReadOnlyList<MonitorInfo>? cachedMonitors;
            lock (_lock)
            {
                if (_cachedMonitors != null)
                {
                    cachedMonitors = _cachedMonitors;
                }
                else if (_refreshTask != null)
                {
                    Log.Debug("MonitorInfoManager: Refresh already in progress, skipping duplicate native call.");
                    return;
                }
                else
                {
                    _ = ObserveRefreshFailureAsync(StartRefreshTaskLocked());
                    return;
                }
            }

            PublishMonitorList(cachedMonitors);
        }

        /// <summary>
        /// Forces a refresh of the monitor list from the system asynchronously.
        /// The refresh is performed on a background thread, and subscribers will be notified via <see cref="MonitorListReady"/> when the list is available.
        /// </summary>
        public async Task<IReadOnlyList<MonitorInfo>> RefreshMonitorsAsync(CancellationToken cancellationToken = default)
        {
            Task<List<MonitorInfo>> refreshTask;
            lock (_lock)
            {
                if (_refreshTask != null)
                {
                    Log.Debug("MonitorInfoManager: Refresh already in progress, joining existing native call.");
                    refreshTask = _refreshTask;
                }
                else
                {
                    Log.Information("Manual refresh requested. Re-scanning monitors.");
                    refreshTask = StartRefreshTaskLocked();
                }
            }

            return await WaitForRefreshAsync(refreshTask, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<MonitorInfo>> ForceRefreshMonitorsAsync(CancellationToken cancellationToken = default)
        {
            return await ForceRefreshMonitorsAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<MonitorInfo>> ForceRefreshMonitorsAsync(TimeSpan phaseTimeout, CancellationToken cancellationToken = default)
        {
            Task<List<MonitorInfo>>? inProgress;
            Task<List<MonitorInfo>> refreshTask;
            lock (_lock)
            {
                inProgress = _refreshTask;
            }

            if (inProgress != null)
            {
                try
                {
                    await WaitForRefreshAsync(inProgress, phaseTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Existing monitor refresh failed before forced refresh; starting a new scan.");
                }
            }

            lock (_lock)
            {
                refreshTask = _refreshTask ?? StartRefreshTaskLocked();
            }

            return await WaitForRefreshAsync(refreshTask, phaseTimeout, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the latest, up-to-date list of monitors from the system (basic info only, no enrichment).
        /// </summary>
        /// <returns>The latest list of <see cref="MonitorInfo"/> objects (basic info only).</returns>
        public List<MonitorInfo> GetLatestMonitorsBasicInfo()
        {
            return _monitorInfoProvider.GetAllMonitorsBasicInfo();
        }

        /// <summary>
        /// Enriches a list of MonitorInfo objects with DDC/CI support and hardware ID.
        /// </summary>
        /// <param name="monitors">The list of monitors to enrich.</param>
        public void EnrichMonitorInfoList(List<MonitorInfo>? monitors)
        {
            if (monitors == null) return;
            foreach (var monitor in monitors)
            {
                monitor.IsDdcCiSupported = _monitorInfoProvider.GetDdcCiSupport(monitor);
                monitor.HardwareId = _monitorInfoProvider.GetHardwareId(monitor);
            }
        }

        /// <inheritdoc />
        public void UpdateCachedMonitors(List<MonitorInfo> monitors)
        {
            if (monitors == null) return;
            lock (_lock)
            {
                _cachedMonitors = monitors;
            }
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// Refreshes the monitor cache by retrieving basic info and enriching each monitor with DDC/CI support and hardware ID.
        /// </summary>
        private List<MonitorInfo> RefreshMonitorsInternal()
        {
            var monitors = _monitorInfoProvider.GetAllMonitorsBasicInfo();
            EnrichMonitorInfoList(monitors);
            return monitors;
        }

        private Task<List<MonitorInfo>> StartRefreshTaskLocked()
        {
            _refreshTask = RefreshMonitorsWorkerAsync();
            return _refreshTask;
        }

        private static async Task ObserveRefreshFailureAsync(Task refreshTask)
        {
            try
            {
                await refreshTask.ConfigureAwait(false);
            }
            catch
            {
                // RefreshMonitorsWorkerAsync already logs the failure. This observer prevents
                // fire-and-forget GetCurrentMonitorsAsync calls from leaving an unobserved fault.
            }
        }

        private static async Task<IReadOnlyList<MonitorInfo>> WaitForRefreshAsync(
            Task<List<MonitorInfo>> refreshTask,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return await refreshTask.ConfigureAwait(false);
            }

            var completed = await Task.WhenAny(refreshTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
            if (completed != refreshTask)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return await refreshTask.ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<MonitorInfo>> WaitForRefreshAsync(
            Task<List<MonitorInfo>> refreshTask,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return await WaitForRefreshAsync(refreshTask, cancellationToken).ConfigureAwait(false);
            }

            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            return await WaitForRefreshAsync(refreshTask, linkedSource.Token).ConfigureAwait(false);
        }

        private async Task<List<MonitorInfo>> RefreshMonitorsWorkerAsync()
        {
            try
            {
                var monitors = await Task.Run(RefreshMonitorsInternal).ConfigureAwait(false);
                lock (_lock)
                {
                    _cachedMonitors = monitors;
                }

                PublishMonitorList(monitors);
                return monitors;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MonitorInfoManager refresh failed.");
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _refreshTask = null;
                }
            }
        }

        private void PublishMonitorList(IReadOnlyList<MonitorInfo> monitors)
        {
            var handlers = MonitorListReady?.GetInvocationList();
            if (handlers == null) return;

            foreach (EventHandler<IReadOnlyList<MonitorInfo>> handler in handlers)
            {
                try
                {
                    handler(this, monitors);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "MonitorListReady subscriber failed.");
                }
            }
        }

        #endregion Private Methods
    }
}
