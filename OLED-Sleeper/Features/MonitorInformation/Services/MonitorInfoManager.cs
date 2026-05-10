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
        private Task? _refreshTask;

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
                    _refreshTask = Task.Run(RefreshMonitorsWorker);
                    return;
                }
            }

            PublishMonitorList(cachedMonitors);
        }

        /// <summary>
        /// Forces a refresh of the monitor list from the system asynchronously.
        /// The refresh is performed on a background thread, and subscribers will be notified via <see cref="MonitorListReady"/> when the list is available.
        /// This method is event-driven and does not return a Task.
        /// </summary>
        public void RefreshMonitorsAsync()
        {
            lock (_lock)
            {
                if (_refreshTask != null)
                {
                    Log.Debug("MonitorInfoManager: Refresh already in progress, skipping duplicate manual refresh.");
                    return;
                }

                Log.Information("Manual refresh requested. Re-scanning monitors.");
                _refreshTask = Task.Run(RefreshMonitorsWorker);
            }
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

        private void RefreshMonitorsWorker()
        {
            try
            {
                var monitors = RefreshMonitorsInternal();
                lock (_lock)
                {
                    _cachedMonitors = monitors;
                    _refreshTask = null;
                }

                PublishMonitorList(monitors);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _refreshTask = null;
                }
                Log.Error(ex, "MonitorInfoManager refresh failed.");
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
