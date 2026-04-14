using System.Runtime.InteropServices;

namespace OLED_Sleeper.Native
{
    /// <summary>
    /// Windows Core Audio (WASAPI) COM interop for enumerating per-process audio sessions.
    /// Used to detect which processes are currently rendering audio so that monitors hosting
    /// media-playback windows can be kept awake.
    /// </summary>
    internal static class AudioInterop
    {
        public enum EDataFlow { eRender = 0 }
        public enum ERole { eMultimedia = 1 }

        public enum AudioSessionState
        {
            AudioSessionStateInactive = 0,
            AudioSessionStateActive = 1,
            AudioSessionStateExpired = 2
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        public class MMDeviceEnumeratorComObject { }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionManager2
        {
            [PreserveSig] int NotImpl0();
            [PreserveSig] int NotImpl1();

            [PreserveSig]
            int GetSessionEnumerator(out IAudioSessionEnumerator ppSessionEnum);
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionEnumerator
        {
            [PreserveSig]
            int GetCount(out int sessionCount);

            [PreserveSig]
            int GetSession(int sessionIndex, out IAudioSessionControl pSession);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl
        {
            [PreserveSig]
            int GetState(out AudioSessionState state);
        }

        [ComImport]
        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out AudioSessionState state);
            [PreserveSig] int NotImpl0();
            [PreserveSig] int NotImpl1();
            [PreserveSig] int NotImpl2();
            [PreserveSig] int NotImpl3();
            [PreserveSig] int NotImpl4();
            [PreserveSig] int NotImpl5();
            [PreserveSig] int NotImpl6();
            [PreserveSig] int NotImpl7();

            [PreserveSig]
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);

            [PreserveSig]
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);

            [PreserveSig]
            int GetProcessId(out uint retVal);

            [PreserveSig]
            int IsSystemSoundsSession();
        }

        public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        public const uint CLSCTX_ALL = 23;
    }
}
