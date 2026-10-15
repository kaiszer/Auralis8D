using System;
using System.Collections.Generic;

namespace Auralis8D.Core.Models
{
    /// <summary>
    /// Holds persistent application preferences, audio engine settings, and selected processes.
    /// Serialized directly to local JSON configuration.
    /// </summary>
    public class AudioSettings
    {
        /// <summary>
        /// Master on/off toggle for the 8D audio processing effect.
        /// </summary>
        public bool MasterEnabled { get; set; } = true;

        /// <summary>
        /// Rotation speed in Hz.
        /// </summary>
        public float RotationSpeedHz { get; set; } = 0.15f;

        /// <summary>
        /// Panning effect depth (0.0 to 1.0).
        /// </summary>
        public float EffectDepth { get; set; } = 0.90f;

        /// <summary>
        /// Mid/Side stereo width multiplier (0.0 to 2.0).
        /// </summary>
        public float StereoWidth { get; set; } = 1.15f;

        /// <summary>
        /// Currently active preset profile name.
        /// </summary>
        public string ActiveProfileName { get; set; } = "Balanced 8D";

        /// <summary>
        /// MMDevice endpoint ID for audio output, or empty for default audio output.
        /// </summary>
        public string OutputDeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Friendly display name of selected audio output device.
        /// </summary>
        public string OutputDeviceName { get; set; } = "Default Audio Endpoint";

        /// <summary>
        /// Whether to remember and auto-select known applications between launches.
        /// </summary>
        public bool RememberSelectedApps { get; set; } = true;

        /// <summary>
        /// List of application executable file names that the user has selected for 8D enhancement.
        /// E.g. "spotify.exe", "chrome.exe", "msedge.exe".
        /// </summary>
        public List<string> SelectedProcessNames { get; set; } = new List<string>();

        /// <summary>
        /// Whether the application starts minimized to system tray or taskbar.
        /// </summary>
        public bool StartMinimized { get; set; } = false;

        /// <summary>
        /// Whether the application is configured to launch on Windows startup.
        /// </summary>
        public bool StartWithWindows { get; set; } = false;
    }
}
