using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Auralis8D.Audio.Interop.WASAPI;

namespace Auralis8D.Audio.Output
{
    /// <summary>
    /// Information about a detected audio output device.
    /// </summary>
    public class AudioDeviceItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Helper to enumerate connected audio playback devices on Windows Core Audio.
    /// </summary>
    public static class DeviceEnumerator
    {
        /// <summary>
        /// Retrieves all active audio render devices on the system.
        /// </summary>
        public static List<AudioDeviceItem> GetAudioRenderDevices()
        {
            var results = new List<AudioDeviceItem>();

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDevice);
                string defaultId = string.Empty;
                if (hr == 0 && defaultDevice != null)
                {
                    defaultDevice.GetId(out defaultId);
                }

                hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out IMMDeviceCollection collection);
                if (hr == 0 && collection != null)
                {
                    collection.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out IMMDevice device) == 0 && device != null)
                        {
                            device.GetId(out string id);
                            string name = GetDeviceFriendlyName(device);

                            results.Add(new AudioDeviceItem
                            {
                                Id = id,
                                Name = string.IsNullOrWhiteSpace(name) ? "Audio Output Device" : name,
                                IsDefault = (id == defaultId)
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback item if enumeration encounters permissions or driver issues
                results.Add(new AudioDeviceItem
                {
                    Id = string.Empty,
                    Name = "Default Windows Playback Device",
                    IsDefault = true
                });
            }

            return results;
        }

        private static string GetDeviceFriendlyName(IMMDevice device)
        {
            try
            {
                if (device.OpenPropertyStore(0 /* STGM_READ */, out IPropertyStore store) == 0 && store != null)
                {
                    PROPERTYKEY key = new PROPERTYKEY
                    {
                        fmtid = AudioGuids.PKEY_Device_FriendlyName,
                        pid = 14
                    };

                    if (store.GetValue(ref key, out PROPVARIANT pv) == 0)
                    {
                        if (pv.pointerVal != IntPtr.Zero)
                        {
                            string? name = Marshal.PtrToStringUni(pv.pointerVal);
                            NativeAudioMethods.PropVariantClear(ref pv);
                            return name ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception) { }

            return string.Empty;
        }
    }
}
