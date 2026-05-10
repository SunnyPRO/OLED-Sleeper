using OLED_Sleeper.Core.Interfaces;
using OLED_Sleeper.Features.MonitorDimming.Commands;
using OLED_Sleeper.Features.MonitorDimming.Services.Interfaces;
using Serilog;

namespace OLED_Sleeper.Features.MonitorDimming.Handlers
{
    /// <summary>
    /// Handles the RestoreBrightnessOnStartupCommand to restore brightness for all monitors left dimmed from a previous session.
    /// </summary>
    public class RestoreBrightnessOnAllMonitorsCommandHandler(
        IMonitorBrightnessStateService monitorBrightnessStateService,
        IMonitorDimmingService monitorDimmingService)
        : ICommandHandler<RestoreBrightnessOnAllMonitorsCommand>
    {
        public async Task HandleAsync(RestoreBrightnessOnAllMonitorsCommand command)
        {
            Log.Information("Checking for monitors with unrestored brightness...");
            var state = monitorBrightnessStateService.LoadState();
            if (state.Any())
            {
                Log.Warning("Found {Count} monitors that were left dimmed from a previous session. Attempting to restore.", state.Count);
                var remaining = new Dictionary<string, uint>();
                foreach (var entry in state)
                {
                    if (!await monitorDimmingService.RestoreBrightnessAsync(entry.Key, entry.Value))
                    {
                        remaining[entry.Key] = entry.Value;
                    }
                }
                monitorBrightnessStateService.SaveState(remaining);
            }
        }
    }
}
