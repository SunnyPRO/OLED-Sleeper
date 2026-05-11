using OLED_Sleeper.Features.MonitorBehavior.Models;
using OLED_Sleeper.Features.MonitorInformation.Models;
using OLED_Sleeper.Features.UserSettings.Models;
using OLED_Sleeper.UI.ViewModels;

namespace OLED_Sleeper.Tests.UI.ViewModels
{
    public class MonitorConfigurationViewModelTests
    {
        private static MonitorConfigurationViewModel CreateVm()
        {
            var info = new MonitorInfo
            {
                HardwareId = "MON-1",
                DeviceName = @"\\.\DISPLAY1",
                DisplayNumber = 1,
                IsDdcCiSupported = true,
                IsPrimary = true
            };
            return new MonitorConfigurationViewModel(info);
        }

        [Fact]
        public void DefaultsToMediaPlaybackActive()
        {
            var vm = CreateVm();

            Assert.True(vm.IsActiveOnMediaPlayback);
        }

        [Fact]
        public void TogglingMediaPlayback_MarksDirty()
        {
            var vm = CreateVm();
            Assert.False(vm.IsDirty);

            vm.IsActiveOnMediaPlayback = !vm.IsActiveOnMediaPlayback;

            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void MediaPlaybackAlone_SatisfiesActiveConditionValidation()
        {
            var vm = CreateVm();
            vm.IsManaged = true;
            vm.Behavior = MonitorBehaviorType.Blackout;
            vm.IdleValue = 30;

            vm.IsActiveOnInput = false;
            vm.IsActiveOnMousePosition = false;
            vm.IsActiveOnActiveWindow = false;
            vm.IsActiveOnMediaPlayback = true;

            Assert.True(vm.IsValid);
            Assert.Equal(string.Empty, vm.ActiveConditionsError);
        }

        [Fact]
        public void NoActiveConditions_FailsValidation()
        {
            var vm = CreateVm();
            vm.IsManaged = true;
            vm.Behavior = MonitorBehaviorType.Blackout;
            vm.IdleValue = 30;

            vm.IsActiveOnInput = false;
            vm.IsActiveOnMousePosition = false;
            vm.IsActiveOnActiveWindow = false;
            vm.IsActiveOnMediaPlayback = false;

            Assert.False(vm.IsValid);
            Assert.NotEqual(string.Empty, vm.ActiveConditionsError);
        }

        [Fact]
        public void ToSettings_PersistsMediaPlaybackFlag()
        {
            var vm = CreateVm();
            vm.IsActiveOnMediaPlayback = false;

            var settings = vm.ToSettings();

            Assert.False(settings.IsActiveOnMediaPlayback);
        }

        [Fact]
        public void ApplySettings_LoadsMediaPlaybackFlag()
        {
            var vm = CreateVm();
            vm.ApplySettings(new MonitorSettings { IsActiveOnMediaPlayback = false });

            Assert.False(vm.IsActiveOnMediaPlayback);
        }
    }
}
