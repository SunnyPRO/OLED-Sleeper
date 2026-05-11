using OLED_Sleeper.Native;

namespace OLED_Sleeper.Tests.Native
{
    public class AudioInteropTests
    {
        [Fact]
        public void IMMDeviceCollection_UsesWindowsMmDeviceApiGuid()
        {
            Assert.Equal(
                new Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
                typeof(AudioInterop.IMMDeviceCollection).GUID);
        }
    }
}
