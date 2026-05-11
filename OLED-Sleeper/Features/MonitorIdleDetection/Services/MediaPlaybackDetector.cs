using OLED_Sleeper.Features.MonitorIdleDetection.Services.Interfaces;
using OLED_Sleeper.Native;
using Serilog;
using System.Runtime.InteropServices;
using System.Windows;

namespace OLED_Sleeper.Features.MonitorIdleDetection.Services
{
    /// <summary>
    /// Uses the Windows Core Audio (WASAPI) session APIs to enumerate active audio sessions,
    /// maps each session back to its owning process, and returns the visible top-level window
    /// rectangles for those processes. The idle detection loop uses these rectangles to
    /// determine whether media is playing on a given monitor.
    /// </summary>
    public class MediaPlaybackDetector : IMediaPlaybackDetector
    {
        private readonly object _cacheLock = new();
        private List<IntPtr> _windowsByProcessCache = new();
        private DateTime _windowCacheTimestamp = DateTime.MinValue;
        private DateTime _lastAudioEnumerationFailureLogUtc = DateTime.MinValue;
        private static readonly TimeSpan WindowCacheTtl = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan AudioEnumerationFailureLogInterval = TimeSpan.FromMinutes(1);

        public IReadOnlyList<Rect> GetPlaybackWindowRects()
        {
            HashSet<uint> playingPids;
            try
            {
                playingPids = GetActiveAudioSessionProcessIds();
            }
            catch (Exception ex)
            {
                LogAudioEnumerationFailure(ex);
                return Array.Empty<Rect>();
            }

            if (playingPids.Count == 0)
            {
                return Array.Empty<Rect>();
            }

            var rects = new List<Rect>();
            foreach (var hwnd in GetTopLevelVisibleWindows())
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (!playingPids.Contains(pid)) continue;

                var rect = GetWindowScreenRect(hwnd);
                if (rect.HasValue && !rect.Value.IsEmpty)
                {
                    rects.Add(rect.Value);
                }
            }
            return rects;
        }

        private static HashSet<uint> GetActiveAudioSessionProcessIds()
        {
            var pids = new HashSet<uint>();
            AudioInterop.IMMDeviceEnumerator? enumerator = null;
            AudioInterop.IMMDeviceCollection? devices = null;

            try
            {
                enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumeratorComObject();
                if (enumerator.EnumAudioEndpoints(AudioInterop.EDataFlow.eRender, AudioInterop.DEVICE_STATE_ACTIVE, out devices) != 0 || devices == null)
                {
                    return pids;
                }

                if (devices.GetCount(out var count) != 0)
                {
                    return pids;
                }

                for (uint i = 0; i < count; i++)
                {
                    AudioInterop.IMMDevice? device = null;
                    try
                    {
                        if (devices.Item(i, out device) != 0 || device == null) continue;
                        AddActiveAudioSessionProcessIds(device, pids);
                    }
                    catch
                    {
                        // One broken endpoint should not disable media detection for the others.
                    }
                    finally
                    {
                        if (device != null) Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                if (devices != null) Marshal.ReleaseComObject(devices);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return pids;
        }

        private void LogAudioEnumerationFailure(Exception ex)
        {
            lock (_cacheLock)
            {
                var now = DateTime.UtcNow;
                if (now - _lastAudioEnumerationFailureLogUtc < AudioEnumerationFailureLogInterval)
                {
                    return;
                }
                _lastAudioEnumerationFailureLogUtc = now;
            }

            Log.Debug(ex, "MediaPlaybackDetector: failed to enumerate audio sessions.");
        }

        private static void AddActiveAudioSessionProcessIds(AudioInterop.IMMDevice device, HashSet<uint> pids)
        {
            AudioInterop.IAudioSessionEnumerator? sessionEnum = null;
            object? sessionManagerObj = null;

            try
            {
                var iid = AudioInterop.IID_IAudioSessionManager2;
                if (device.Activate(ref iid, AudioInterop.CLSCTX_ALL, IntPtr.Zero, out sessionManagerObj) != 0 || sessionManagerObj == null)
                {
                    return;
                }

                var sessionManager = (AudioInterop.IAudioSessionManager2)sessionManagerObj;
                if (sessionManager.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null)
                {
                    return;
                }

                if (sessionEnum.GetCount(out var count) != 0)
                {
                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    AudioInterop.IAudioSessionControl? control = null;
                    try
                    {
                        if (sessionEnum.GetSession(i, out control) != 0 || control == null) continue;

                        if (control is not AudioInterop.IAudioSessionControl2 control2) continue;

                        if (control2.GetState(out var state) != 0) continue;
                        if (state != AudioInterop.AudioSessionState.AudioSessionStateActive) continue;

                        // IsSystemSoundsSession returns S_OK (0) if the session IS system sounds,
                        // S_FALSE (1) otherwise. Skip system sounds — no real owning process.
                        var systemSoundsResult = control2.IsSystemSoundsSession();
                        if (systemSoundsResult <= 0) continue;

                        if (control2.GetProcessId(out var pid) == 0 && pid != 0)
                        {
                            pids.Add(pid);
                        }
                    }
                    finally
                    {
                        if (control != null) Marshal.ReleaseComObject(control);
                    }
                }
            }
            finally
            {
                if (sessionEnum != null) Marshal.ReleaseComObject(sessionEnum);
                // sessionManager and sessionManagerObj are the same RCW (a managed cast does QI
                // but returns the same underlying wrapper). Release once.
                if (sessionManagerObj != null) Marshal.ReleaseComObject(sessionManagerObj);
            }
        }

        private IReadOnlyList<IntPtr> GetTopLevelVisibleWindows()
        {
            lock (_cacheLock)
            {
                if (DateTime.UtcNow - _windowCacheTimestamp < WindowCacheTtl)
                {
                    return _windowsByProcessCache;
                }

                var list = new List<IntPtr>();
                NativeMethods.EnumWindows((hwnd, _) =>
                {
                    if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                    // Skip tool/owned windows — a media app's renderable surface is always a top-level window.
                    if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;
                    list.Add(hwnd);
                    return true;
                }, IntPtr.Zero);

                _windowsByProcessCache = list;
                _windowCacheTimestamp = DateTime.UtcNow;
                return list;
            }
        }

        private static Rect? GetWindowScreenRect(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd)) return null;

            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out var dwmRect, Marshal.SizeOf(typeof(NativeMethods.Rect))) == 0)
            {
                return dwmRect.ToWindowsRect();
            }

            if (NativeMethods.GetWindowRect(hwnd, out var nativeRect))
            {
                return nativeRect.ToWindowsRect();
            }
            return null;
        }
    }
}
