using System.Windows;

namespace OLED_Sleeper.Features.MonitorIdleDetection.Services.Interfaces
{
    /// <summary>
    /// Detects the on-screen rectangles of windows belonging to processes that are currently
    /// rendering audio (via WASAPI). Callers intersect these rectangles with monitor bounds to
    /// decide whether a monitor should be kept awake because media is playing on it.
    /// </summary>
    public interface IMediaPlaybackDetector
    {
        /// <summary>
        /// Returns the set of top-level visible window rectangles owned by processes that are
        /// currently producing audio. An empty list means no media is actively playing.
        /// </summary>
        IReadOnlyList<Rect> GetPlaybackWindowRects();
    }
}
