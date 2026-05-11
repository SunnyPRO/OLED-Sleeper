namespace OLED_Sleeper.Features.MonitorState.Services.Interfaces
{
    /// <summary>
    /// Subscribes to OS power-mode and session-switch events and coordinates monitor
    /// restoration when the system suspends or resumes.
    /// </summary>
    public interface IPowerEventMonitor
    {
        /// <summary>Starts listening for power/session events.</summary>
        void Start();

        /// <summary>Stops listening for power/session events.</summary>
        void Stop();
    }
}
