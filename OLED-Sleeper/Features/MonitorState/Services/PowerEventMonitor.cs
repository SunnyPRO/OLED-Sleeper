using Microsoft.Win32;
using OLED_Sleeper.Core;
using OLED_Sleeper.Core.Interfaces;
using OLED_Sleeper.Features.MonitorDimming.Commands;
using OLED_Sleeper.Features.MonitorInformation.Services.Interfaces;
using OLED_Sleeper.Features.MonitorIdleDetection.Services.Interfaces;
using OLED_Sleeper.Features.MonitorState.Services.Interfaces;
using OLED_Sleeper.Features.UserSettings.Services.Interfaces;
using Serilog;

namespace OLED_Sleeper.Features.MonitorState.Services
{
    /// <summary>
    /// Listens for <see cref="SystemEvents.PowerModeChanged"/> and <see cref="SystemEvents.SessionSwitch"/>
    /// notifications so the app can proactively undim monitors before the OS suspends and re-apply the
    /// correct state on resume. Without this, the dimmed brightness value can end up mistaken for the
    /// original on wake, leaving the monitor permanently dimmed.
    /// </summary>
    public class PowerEventMonitor : IPowerEventMonitor, IDisposable
    {
        private readonly IMediator _mediator;
        private readonly IMonitorInfoManager _monitorInfoManager;
        private readonly IMonitorIdleDetectionService _idleDetectionService;
        private readonly IMonitorSettingsFileService _settingsFileService;
        private bool _isStarted;

        public PowerEventMonitor(
            IMediator mediator,
            IMonitorInfoManager monitorInfoManager,
            IMonitorIdleDetectionService idleDetectionService,
            IMonitorSettingsFileService settingsFileService)
        {
            _mediator = mediator;
            _monitorInfoManager = monitorInfoManager;
            _idleDetectionService = idleDetectionService;
            _settingsFileService = settingsFileService;
        }

        public void Start()
        {
            if (_isStarted) return;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _isStarted = true;
            Log.Information("PowerEventMonitor started.");
        }

        public void Stop()
        {
            if (!_isStarted) return;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _isStarted = false;
            Log.Information("PowerEventMonitor stopped.");
        }

        /// <summary>Test hook: invokes suspend handling without a real OS event.</summary>
        public void HandleSuspend() => OnSuspendInternal();

        /// <summary>Test hook: invokes resume handling without a real OS event.</summary>
        public void HandleResume() => OnResumeInternal();

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            try
            {
                switch (e.Mode)
                {
                    case PowerModes.Suspend:
                        OnSuspendInternal();
                        break;
                    case PowerModes.Resume:
                        OnResumeInternal();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error in PowerModeChanged handler ({Mode}).", e.Mode);
            }
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            try
            {
                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionLock:
                        Log.Information("Session locked.");
                        break;
                    case SessionSwitchReason.SessionUnlock:
                        Log.Information("Session unlocked - restoring monitor state.");
                        ResyncMonitorStateAfterResume();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error in SessionSwitch handler ({Reason}).", e.Reason);
            }
        }

        private void OnSuspendInternal()
        {
            // Best-effort undim/unblack before the OS puts monitors to sleep. The persisted
            // brightness_state.json is preserved so that on resume we can still restore original
            // brightness for any monitor we didn't manage to undim in time.
            Log.Information("System suspending - requesting restore of all monitors.");
            ApplicationNotifications.TriggerRestoreAllMonitors();
        }

        private void OnResumeInternal()
        {
            Log.Information("System resuming - re-synchronizing monitor state.");
            ResyncMonitorStateAfterResume();
        }

        private void ResyncMonitorStateAfterResume()
        {
            // 1. Force a fresh enumeration — physical monitor handles from before the power
            //    transition may be stale.
            _monitorInfoManager.RefreshMonitorsAsync();

            // 2. Re-apply idle detection settings so per-monitor state machines reset to Active.
            var settings = _settingsFileService.LoadSettings();
            _idleDetectionService.UpdateSettings(settings);

            // 3. Restore any brightness entries still recorded on disk (covers the case where
            //    the suspend-time undim never ran or failed because handles were invalidated).
            _ = _mediator.SendAsync(new RestoreBrightnessOnAllMonitorsCommand());
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
