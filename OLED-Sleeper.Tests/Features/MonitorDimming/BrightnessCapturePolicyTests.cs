using OLED_Sleeper.Features.MonitorDimming.Services;

namespace OLED_Sleeper.Tests.Features.MonitorDimming
{
    public class BrightnessCapturePolicyTests
    {
        [Fact]
        public void Captures_WhenBrightAndNotYetTracked()
        {
            Assert.True(BrightnessCapturePolicy.ShouldCaptureOriginal(currentBrightness: 90, dimLevel: 15, alreadyTracked: false));
        }

        [Fact]
        public void RefusesCapture_WhenAlreadyTracked()
        {
            Assert.False(BrightnessCapturePolicy.ShouldCaptureOriginal(currentBrightness: 90, dimLevel: 15, alreadyTracked: true));
        }

        [Fact]
        public void RefusesCapture_WhenCurrentAtDimTarget()
        {
            // Guards against capturing a dimmed reading as "original" after a stale re-dim
            // (e.g. post-resume from sleep when the app temporarily lost track of the monitor).
            Assert.False(BrightnessCapturePolicy.ShouldCaptureOriginal(currentBrightness: 15, dimLevel: 15, alreadyTracked: false));
        }

        [Fact]
        public void RefusesCapture_WhenCurrentBelowDimTarget()
        {
            Assert.False(BrightnessCapturePolicy.ShouldCaptureOriginal(currentBrightness: 5, dimLevel: 15, alreadyTracked: false));
        }

        [Fact]
        public void RefusesCapture_WhenAlreadyTrackedEvenIfReadingIsBright()
        {
            Assert.False(BrightnessCapturePolicy.ShouldCaptureOriginal(currentBrightness: 100, dimLevel: 15, alreadyTracked: true));
        }
    }
}
