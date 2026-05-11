namespace OLED_Sleeper.Features.MonitorDimming.Services
{
    /// <summary>
    /// Decides whether the dimming service should treat the current DDC/CI brightness reading
    /// as the "original" value to restore later. The rules here exist to defend against the
    /// sleep/resume failure mode where a stale dim re-capture would overwrite the real original
    /// with the dimmed value and leave the monitor permanently dim after wake.
    /// </summary>
    public static class BrightnessCapturePolicy
    {
        /// <summary>
        /// Returns <c>true</c> when the service should persist <paramref name="currentBrightness"/>
        /// as the monitor's pre-dim brightness.
        /// </summary>
        /// <param name="currentBrightness">Brightness reported by the monitor right now.</param>
        /// <param name="dimLevel">The brightness level about to be written.</param>
        /// <param name="alreadyTracked">Whether a pre-dim brightness has already been captured for this monitor.</param>
        public static bool ShouldCaptureOriginal(uint currentBrightness, uint dimLevel, bool alreadyTracked)
        {
            if (alreadyTracked) return false;
            if (currentBrightness <= dimLevel) return false;
            return true;
        }
    }
}
