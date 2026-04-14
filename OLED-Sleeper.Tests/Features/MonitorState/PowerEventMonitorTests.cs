using Moq;
using OLED_Sleeper.Core.Interfaces;
using OLED_Sleeper.Features.MonitorDimming.Commands;
using OLED_Sleeper.Features.MonitorIdleDetection.Services.Interfaces;
using OLED_Sleeper.Features.MonitorInformation.Services.Interfaces;
using OLED_Sleeper.Features.MonitorState.Services;
using OLED_Sleeper.Features.UserSettings.Models;
using OLED_Sleeper.Features.UserSettings.Services.Interfaces;

namespace OLED_Sleeper.Tests.Features.MonitorState
{
    public class PowerEventMonitorTests
    {
        private readonly Mock<IMediator> _mediator = new();
        private readonly Mock<IMonitorInfoManager> _monitorInfoManager = new();
        private readonly Mock<IMonitorIdleDetectionService> _idleDetection = new();
        private readonly Mock<IMonitorSettingsFileService> _settingsFile = new();
        private readonly PowerEventMonitor _sut;

        public PowerEventMonitorTests()
        {
            _sut = new PowerEventMonitor(
                _mediator.Object,
                _monitorInfoManager.Object,
                _idleDetection.Object,
                _settingsFile.Object);
        }

        [Fact]
        public void HandleResume_RefreshesMonitors_ReappliesIdleSettings_AndRestoresBrightness()
        {
            var persisted = new List<MonitorSettings>
            {
                new() { HardwareId = "MON-1", IsManaged = true }
            };
            _settingsFile.Setup(s => s.LoadSettings()).Returns(persisted);

            _sut.HandleResume();

            _monitorInfoManager.Verify(m => m.RefreshMonitorsAsync(), Times.Once);
            _idleDetection.Verify(i => i.UpdateSettings(persisted), Times.Once);
            _mediator.Verify(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()), Times.Once);
        }

        [Fact]
        public void HandleSuspend_TriggersRestoreAllMonitorsNotification()
        {
            var invoked = 0;
            void Handler() => invoked++;
            OLED_Sleeper.Core.ApplicationNotifications.RestoreAllMonitorsRequested += Handler;
            try
            {
                _sut.HandleSuspend();
            }
            finally
            {
                OLED_Sleeper.Core.ApplicationNotifications.RestoreAllMonitorsRequested -= Handler;
            }

            Assert.Equal(1, invoked);
        }
    }
}
