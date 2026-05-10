using OLED_Sleeper.Features.MonitorInformation.Models;

namespace OLED_Sleeper.Features.MonitorInformation.Services.Interfaces
{
    /// <summary>
    /// Defines the contract for managing and refreshing monitor information from the system.
    /// </summary>
    public interface IMonitorInfoManager
    {
        /// <summary>
        /// Begins asynchronous retrieval and enrichment of the monitor list.
        /// Subscribers will be notified via <see cref="MonitorListReady"/> when the list is available.
        /// If the cache is already populated, the event is raised immediately.
        /// </summary>
        void GetCurrentMonitorsAsync();

        /// <summary>
        /// Raised when the monitor list has been retrieved and enriched.
        /// </summary>
        event EventHandler<IReadOnlyList<MonitorInfo>> MonitorListReady;

        /// <summary>
        /// Forces a refresh of the monitor list from the system asynchronously.
        /// The refresh is performed on a background thread, and subscribers will be notified via <see cref="MonitorListReady"/> when the list is available.
        /// </summary>
        /// <param name="cancellationToken">Cancels waiting for the refresh result; the native refresh may continue in the background.</param>
        /// <returns>The freshly enumerated and enriched monitor list.</returns>
        Task<IReadOnlyList<MonitorInfo>> RefreshMonitorsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits for any current refresh to finish, then performs or joins the next refresh that
        /// starts after this request. Use this after power/display transitions where pre-existing
        /// monitor handles may be stale.
        /// </summary>
        /// <param name="cancellationToken">Cancels waiting for the refresh result; the native refresh may continue in the background.</param>
        /// <returns>The freshly enumerated and enriched monitor list.</returns>
        Task<IReadOnlyList<MonitorInfo>> ForceRefreshMonitorsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the latest, up-to-date list of monitors from the system (basic info only, no enrichment).
        /// </summary>
        /// <returns>A list of <see cref="MonitorInfo"/> objects representing the latest monitors.</returns>
        List<MonitorInfo> GetLatestMonitorsBasicInfo();

        /// <summary>
        /// Enriches a list of MonitorInfo objects with DDC/CI support and hardware ID.
        /// </summary>
        /// <param name="monitors">The list of monitors to enrich.</param>
        void EnrichMonitorInfoList(List<MonitorInfo> monitors);

        /// <summary>
        /// Replaces the cached monitor list with an externally-enriched snapshot. Used by
        /// pollers (e.g. <c>MonitorStateWatcher</c>) that already paid the DDC/CI enumeration
        /// cost and want their fresh data to be the answer for the next
        /// <see cref="GetCurrentMonitorsAsync"/> call so downstream consumers don't keep
        /// reading stale capabilities.
        /// </summary>
        /// <param name="monitors">The freshly enriched monitor list to install as the cache.</param>
        void UpdateCachedMonitors(List<MonitorInfo> monitors);
    }
}
