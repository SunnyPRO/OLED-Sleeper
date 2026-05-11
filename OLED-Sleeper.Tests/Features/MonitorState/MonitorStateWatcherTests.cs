using OLED_Sleeper.Features.MonitorInformation.Models;
using OLED_Sleeper.Features.MonitorState.Services;
using System.Windows;

namespace OLED_Sleeper.Tests.Features.MonitorState
{
    public class MonitorStateWatcherTests
    {
        [Fact]
        public void AreBasicMonitorListsEqual_WhenSameDeviceNameButBoundsChanged_ReturnsFalse()
        {
            var oldMonitors = new List<MonitorInfo>
            {
                new() { DeviceName = @"\\.\DISPLAY1", DisplayNumber = 1, Bounds = new Rect(0, 0, 1920, 1080) }
            };
            var newMonitors = new List<MonitorInfo>
            {
                new() { DeviceName = @"\\.\DISPLAY1", DisplayNumber = 1, Bounds = new Rect(0, 0, 3840, 2160) }
            };

            Assert.False(MonitorStateWatcher.AreBasicMonitorListsEqual(oldMonitors, newMonitors));
        }

        [Fact]
        public void FilterManageableMonitors_ReturnsStableDisplayNumberOrder()
        {
            var monitors = new List<MonitorInfo>
            {
                new() { DeviceName = @"\\.\DISPLAY3", HardwareId = "MON-3", DisplayNumber = 3, Bounds = new Rect(0, 0, 100, 100) },
                new() { DeviceName = @"\\.\DISPLAY1", HardwareId = "MON-1", DisplayNumber = 1, Bounds = new Rect(200, 0, 100, 100) },
                new() { DeviceName = @"\\.\DISPLAY2", HardwareId = "MON-2", DisplayNumber = 2, Bounds = new Rect(100, 0, 100, 100) }
            };

            var manageable = MonitorStateWatcher.FilterManageableMonitors(monitors);

            Assert.Equal(new[] { 1, 2, 3 }, manageable.Select(m => m.DisplayNumber));
        }

        [Fact]
        public void AreEnrichedMonitorListsEqual_WhenSameHardwareButBoundsChanged_ReturnsFalse()
        {
            var oldMonitors = new List<MonitorInfo>
            {
                new()
                {
                    DeviceName = @"\\.\DISPLAY1",
                    HardwareId = "MON-1",
                    IsDdcCiSupported = true,
                    DisplayNumber = 1,
                    Bounds = new Rect(0, 0, 1920, 1080)
                }
            };
            var newMonitors = new List<MonitorInfo>
            {
                new()
                {
                    DeviceName = @"\\.\DISPLAY1",
                    HardwareId = "MON-1",
                    IsDdcCiSupported = true,
                    DisplayNumber = 1,
                    Bounds = new Rect(1920, 0, 1920, 1080)
                }
            };

            Assert.False(MonitorStateWatcher.AreEnrichedMonitorListsEqual(oldMonitors, newMonitors));
        }

        [Fact]
        public void AreEnrichedMonitorListsEqual_WhenSameHardwareButDisplayNumberChanged_ReturnsFalse()
        {
            var oldMonitors = new List<MonitorInfo>
            {
                new()
                {
                    DeviceName = @"\\.\DISPLAY1",
                    HardwareId = "MON-1",
                    IsDdcCiSupported = true,
                    DisplayNumber = 1,
                    Bounds = new Rect(0, 0, 1920, 1080)
                }
            };
            var newMonitors = new List<MonitorInfo>
            {
                new()
                {
                    DeviceName = @"\\.\DISPLAY1",
                    HardwareId = "MON-1",
                    IsDdcCiSupported = true,
                    DisplayNumber = 2,
                    Bounds = new Rect(0, 0, 1920, 1080)
                }
            };

            Assert.False(MonitorStateWatcher.AreEnrichedMonitorListsEqual(oldMonitors, newMonitors));
        }
    }
}
