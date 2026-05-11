using OLED_Sleeper.Features.MonitorInformation.Models;
using OLED_Sleeper.UI.Services;
using System.Windows;

namespace OLED_Sleeper.Tests.UI.Services
{
    public class MonitorLayoutServiceTests
    {
        [Fact]
        public void CreateLayout_OrdersMonitorsByDisplayNumber()
        {
            var service = new MonitorLayoutService();
            var monitors = new List<MonitorInfo>
            {
                new() { DeviceName = @"\\.\DISPLAY3", HardwareId = "MON-3", DisplayNumber = 3, Bounds = new Rect(0, 0, 100, 100) },
                new() { DeviceName = @"\\.\DISPLAY1", HardwareId = "MON-1", DisplayNumber = 1, Bounds = new Rect(200, 0, 100, 100) },
                new() { DeviceName = @"\\.\DISPLAY2", HardwareId = "MON-2", DisplayNumber = 2, Bounds = new Rect(100, 0, 100, 100) }
            };

            var layout = service.CreateLayout(monitors, 300, 100);

            Assert.Equal(new[] { 1, 2, 3 }, layout.Select(m => m.DisplayNumber));
        }
    }
}
