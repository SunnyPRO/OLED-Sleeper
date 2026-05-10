using OLED_Sleeper.Features.MonitorInformation.Models;
using OLED_Sleeper.Features.MonitorInformation.Services;
using OLED_Sleeper.Features.MonitorInformation.Services.Interfaces;
using System.Windows;

namespace OLED_Sleeper.Tests.Features.MonitorInformation
{
    public class MonitorInfoManagerTests
    {
        [Fact]
        public async Task ForceRefreshMonitorsAsync_WaitsForInProgressRefresh_ThenRunsFreshScan()
        {
            var provider = new BlockingFirstScanProvider();
            var manager = new MonitorInfoManager(provider);

            manager.GetCurrentMonitorsAsync();
            await provider.FirstScanStarted.Task;

            var forceRefresh = manager.ForceRefreshMonitorsAsync();

            Assert.False(forceRefresh.IsCompleted);

            provider.AllowFirstScan.SetResult();
            var monitors = await forceRefresh;

            Assert.Equal(2, provider.BasicScanCount);
            Assert.Equal("MON-2", monitors.Single().HardwareId);
        }

        private sealed class BlockingFirstScanProvider : IMonitorInfoProvider
        {
            public readonly TaskCompletionSource FirstScanStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource AllowFirstScan = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int BasicScanCount;

            public List<MonitorInfo> GetAllMonitorsBasicInfo()
            {
                var scanNumber = Interlocked.Increment(ref BasicScanCount);
                if (scanNumber == 1)
                {
                    FirstScanStarted.SetResult();
                    AllowFirstScan.Task.GetAwaiter().GetResult();
                }

                return new List<MonitorInfo>
                {
                    new()
                    {
                        DeviceName = @"\\.\DISPLAY1",
                        DisplayNumber = scanNumber,
                        Bounds = new Rect(0, 0, 100, 100)
                    }
                };
            }

            public bool GetDdcCiSupport(MonitorInfo monitor) => true;

            public string GetHardwareId(MonitorInfo monitor) => $"MON-{monitor.DisplayNumber}";
        }
    }
}
