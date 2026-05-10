using Moq;
using OLED_Sleeper.Core.Interfaces;
using OLED_Sleeper.Features.MonitorDimming.Commands;
using OLED_Sleeper.Features.MonitorInformation.Models;
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
        public async Task HandleResume_WaitsForMonitorRefreshBeforeReapplyingSettingsAndRestoringBrightness()
        {
            var persisted = new List<MonitorSettings>
            {
                new() { HardwareId = "MON-1", IsManaged = true }
            };
            var refreshCompletion = new TaskCompletionSource<IReadOnlyList<MonitorInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _settingsFile.Setup(s => s.LoadSettings()).Returns(persisted);
            var refreshRequested = false;
            _monitorInfoManager
                .Setup(m => m.ForceRefreshMonitorsAsync(TimeSpan.FromSeconds(12), It.IsAny<CancellationToken>()))
                .Callback(() => refreshRequested = true)
                .Returns(refreshCompletion.Task);
            _mediator
                .Setup(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()))
                .Returns(Task.CompletedTask);

            var resyncTask = _sut.HandleResumeAsync();

            Assert.True(refreshRequested);
            _idleDetection.Verify(i => i.UpdateSettings(It.IsAny<List<MonitorSettings>>()), Times.Never);
            _mediator.Verify(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()), Times.Never);

            refreshCompletion.SetResult(new List<MonitorInfo>());
            await resyncTask;

            _monitorInfoManager.Verify(m => m.ForceRefreshMonitorsAsync(TimeSpan.FromSeconds(12), It.IsAny<CancellationToken>()), Times.Once);
            _idleDetection.Verify(i => i.UpdateSettings(persisted), Times.Once);
            _mediator.Verify(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()), Times.Once);
        }

        [Fact]
        public async Task HandleResume_WhenDuplicateArrivesDuringRefresh_RunsQueuedFollowUp()
        {
            var persisted = new List<MonitorSettings>
            {
                new() { HardwareId = "MON-1", IsManaged = true }
            };
            var firstRefresh = new TaskCompletionSource<IReadOnlyList<MonitorInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRefresh = new TaskCompletionSource<IReadOnlyList<MonitorInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshCalls = 0;

            _settingsFile.Setup(s => s.LoadSettings()).Returns(persisted);
            _monitorInfoManager
                .Setup(m => m.ForceRefreshMonitorsAsync(TimeSpan.FromSeconds(12), It.IsAny<CancellationToken>()))
                .Returns(() => Interlocked.Increment(ref refreshCalls) == 1 ? firstRefresh.Task : secondRefresh.Task);
            _mediator
                .Setup(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()))
                .Returns(Task.CompletedTask);

            var resyncTask = _sut.HandleResumeAsync();
            await _sut.HandleResumeAsync();

            Assert.Equal(1, refreshCalls);

            firstRefresh.SetResult(new List<MonitorInfo>());
            await WaitUntilAsync(() => Volatile.Read(ref refreshCalls) == 2);
            secondRefresh.SetResult(new List<MonitorInfo>());
            await resyncTask;

            _monitorInfoManager.Verify(m => m.ForceRefreshMonitorsAsync(TimeSpan.FromSeconds(12), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _idleDetection.Verify(i => i.UpdateSettings(persisted), Times.Exactly(2));
            _mediator.Verify(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()), Times.Exactly(2));
        }

        [Fact]
        public async Task HandleResume_WhenMonitorRefreshFails_StillReappliesSettingsAndRestoresBrightness()
        {
            var persisted = new List<MonitorSettings>
            {
                new() { HardwareId = "MON-1", IsManaged = true }
            };
            _settingsFile.Setup(s => s.LoadSettings()).Returns(persisted);
            _monitorInfoManager
                .Setup(m => m.ForceRefreshMonitorsAsync(TimeSpan.FromSeconds(12), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Driver not ready"));
            _mediator
                .Setup(m => m.SendAsync(It.IsAny<RestoreBrightnessOnAllMonitorsCommand>()))
                .Returns(Task.CompletedTask);

            await _sut.HandleResumeAsync();

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

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!condition())
            {
                if (timeout.IsCancellationRequested)
                {
                    throw new TimeoutException("Condition was not reached before timeout.");
                }
                await Task.Delay(10);
            }
        }
    }
}
