using OLED_Sleeper.Features.MonitorDimming.Services;
using System.IO;

namespace OLED_Sleeper.Tests.Features.MonitorDimming
{
    public class MonitorBrightnessStateServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _stateFilePath;

        public MonitorBrightnessStateServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OLED-Sleeper-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _stateFilePath = Path.Combine(_tempDir, "brightness_state.json");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void LoadState_ReturnsEmpty_WhenFileDoesNotExist()
        {
            var sut = new MonitorBrightnessStateService(_stateFilePath);

            var state = sut.LoadState();

            Assert.Empty(state);
        }

        [Fact]
        public void SaveAndLoad_RoundTripsBrightnessMap()
        {
            var sut = new MonitorBrightnessStateService(_stateFilePath);
            var original = new Dictionary<string, uint>
            {
                ["MON-A"] = 80,
                ["MON-B"] = 45
            };

            sut.SaveState(original);

            var loaded = new MonitorBrightnessStateService(_stateFilePath).LoadState();
            Assert.Equal(2, loaded.Count);
            Assert.Equal(80u, loaded["MON-A"]);
            Assert.Equal(45u, loaded["MON-B"]);
        }

        [Fact]
        public void SaveState_WritesAtomically_NoLingeringTempFile()
        {
            var sut = new MonitorBrightnessStateService(_stateFilePath);
            sut.SaveState(new Dictionary<string, uint> { ["MON-A"] = 50 });

            var tempFile = _stateFilePath + ".tmp";
            Assert.False(File.Exists(tempFile), "temp file should have been renamed over the state file");
            Assert.True(File.Exists(_stateFilePath));
        }

        [Fact]
        public void LoadState_RecoversFromCorruptJson()
        {
            File.WriteAllText(_stateFilePath, "{not-valid-json");
            var sut = new MonitorBrightnessStateService(_stateFilePath);

            var state = sut.LoadState();

            Assert.Empty(state);
        }
    }
}
