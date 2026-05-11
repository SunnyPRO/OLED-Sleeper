using Moq;
using OLED_Sleeper.Features.MonitorDimming.Commands;
using OLED_Sleeper.Features.MonitorDimming.Handlers;
using OLED_Sleeper.Features.MonitorDimming.Services.Interfaces;

namespace OLED_Sleeper.Tests.Features.MonitorDimming
{
    public class RestoreBrightnessOnAllMonitorsCommandHandlerTests
    {
        [Fact]
        public async Task HandleAsync_WhenRestoreSucceeds_ClearsRestoredState()
        {
            var stateService = new Mock<IMonitorBrightnessStateService>();
            var dimmingService = new Mock<IMonitorDimmingService>();
            stateService.Setup(s => s.LoadState()).Returns(new Dictionary<string, uint> { ["MON-1"] = 75 });
            dimmingService.Setup(s => s.RestoreBrightnessAsync("MON-1", 75)).ReturnsAsync(true);

            var handler = new RestoreBrightnessOnAllMonitorsCommandHandler(stateService.Object, dimmingService.Object);

            await handler.HandleAsync(new RestoreBrightnessOnAllMonitorsCommand());

            stateService.Verify(s => s.SaveState(It.Is<Dictionary<string, uint>>(state => state.Count == 0)), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_WhenRestoreFails_PreservesUnrestoredState()
        {
            var stateService = new Mock<IMonitorBrightnessStateService>();
            var dimmingService = new Mock<IMonitorDimmingService>();
            stateService.Setup(s => s.LoadState()).Returns(new Dictionary<string, uint>
            {
                ["MON-1"] = 75,
                ["MON-2"] = 40
            });
            dimmingService.Setup(s => s.RestoreBrightnessAsync("MON-1", 75)).ReturnsAsync(false);
            dimmingService.Setup(s => s.RestoreBrightnessAsync("MON-2", 40)).ReturnsAsync(true);

            var handler = new RestoreBrightnessOnAllMonitorsCommandHandler(stateService.Object, dimmingService.Object);

            await handler.HandleAsync(new RestoreBrightnessOnAllMonitorsCommand());

            stateService.Verify(s => s.SaveState(It.Is<Dictionary<string, uint>>(state =>
                state.Count == 1 && state["MON-1"] == 75)), Times.Once);
        }
    }
}
