using System;
using System.Runtime.InteropServices;

namespace Auralis8D.Audio.Interop.WASAPI
{
    public enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    public enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    public enum AUDCLNT_SHAREMODE
    {
        AUDCLNT_SHAREMODE_SHARED = 0,
        AUDCLNT_SHAREMODE_EXCLUSIVE = 1
    }

    [Flags]
    public enum AUDCLNT_STREAMFLAGS : uint
    {
        AUDCLNT_STREAMFLAGS_NONE = 0x00000000,
        AUDCLNT_STREAMFLAGS_CROSSPROCESS = 0x00010000,
        AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000,
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000,
        AUDCLNT_STREAMFLAGS_NOPERSIST = 0x00080000,
        AUDCLNT_STREAMFLAGS_RATEADJUST = 0x00100000,
        AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000,
        AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000
    }

    [Flags]
    public enum AUDCLNT_BUFFERFLAGS : uint
    {
        AUDCLNT_BUFFERFLAGS_NONE = 0x0,
        AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1,
        AUDCLNT_BUFFERFLAGS_SILENT = 0x2,
        AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4
    }

    public enum PROCESS_LOOPBACK_MODE
    {
        PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0,
        PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1
    }

    public enum AUDIOCLIENT_ACTIVATION_TYPE
    {
        AUDIOCLIENT_ACTIVATION_TYPE_DEFAULT = 0,
        AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
    {
        public uint TargetProcessId;
        public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        [FieldOffset(0)]
        public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;

        [FieldOffset(4)]
        public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pointerVal;
        public IntPtr pointerVal2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WAVEFORMATEXTENSIBLE
    {
        public WAVEFORMATEX Format;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    public static class AudioGuids
    {
        public const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");
        public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
        public static readonly Guid PKEY_Device_FriendlyName = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator
    {
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        int Initialize(AUDCLNT_SHAREMODE ShareMode, AUDCLNT_STREAMFLAGS StreamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, ref Guid AudioSessionGuid);
        int GetBufferSize(out uint pNumBufferFrames);
        int GetStreamLatency(out long phnsLatency);
        int GetCurrentPadding(out uint pNumPaddingFrames);
        int IsFormatSupported(AUDCLNT_SHAREMODE ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        int GetMixFormat(out IntPtr ppDeviceFormat);
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr eventHandle);
        int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out AUDCLNT_BUFFERFLAGS pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        int ReleaseBuffer(uint NumFramesRead);
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioRenderClient
    {
        int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
        int ReleaseBuffer(uint NumFramesWritten, AUDCLNT_BUFFERFLAGS dwFlags);
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IAudioSessionControl SessionControl);
        int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out ISimpleAudioVolume AudioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        int RegisterSessionNotification(IntPtr NewSessionNotifications);
        int UnregisterSessionNotification(IntPtr NewSessionNotifications);
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
        int GetCount(out int SessionCount);
        int GetSession(int SessionIndex, out IAudioSessionControl Session);
    }

    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl
    {
        int GetState(out uint pRetVal);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        int GetGroupingParam(out Guid pRetVal);
        int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        int RegisterAudioSessionNotification(IntPtr NewNotifications);
        int UnregisterAudioSessionNotification(IntPtr NewNotifications);
    }

    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2
    {
        // IAudioSessionControl methods in vtable order
        int GetState(out uint pRetVal);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
        int GetGroupingParam(out Guid pRetVal);
        int SetGroupingParam(ref Guid Override, ref Guid EventContext);
        int RegisterAudioSessionNotification(IntPtr NewNotifications);
        int UnregisterAudioSessionNotification(IntPtr NewNotifications);

        // IAudioSessionControl2 methods
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        int GetProcessId(out uint pRetVal);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISimpleAudioVolume
    {
        int SetMasterVolume(float fLevel, ref Guid EventContext);
        int GetMasterVolume(out float pfLevel);
        int SetMute(bool bMute, ref Guid EventContext);
        int GetMute(out bool pbMute);
    }

    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMeterInformation
    {
        int GetPeakValue(out float pfPeak);
        int GetMeteringChannelCount(out uint pnChannelCount);
        int GetChannelsPeakValues(uint u32ChannelCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] afPeakValues);
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    }

    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceAsyncOperation
    {
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceCompletionHandler
    {
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    public static class NativeAudioMethods
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
        public static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid riid,
            IntPtr currentActivationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
        public static extern int PropVariantClear(ref PROPVARIANT pvar);
    }
}
