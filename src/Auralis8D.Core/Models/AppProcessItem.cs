namespace Auralis8D.Core.Models
{
    /// <summary>
    /// Information model representing a detected Windows application process that produces audio.
    /// </summary>
    public class AppProcessItem
    {
        /// <summary>
        /// Operating system Process Identifier (PID).
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Executable name (e.g. "Spotify.exe", "msedge.exe").
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>
        /// Friendly title or main window caption.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Full path to the executable file on disk, if accessible.
        /// </summary>
        public string ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// Whether the user has chosen to enhance this application with 8D audio.
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// Whether this application session is actively rendering audio in Windows.
        /// </summary>
        public bool IsActiveAudio { get; set; }

        /// <summary>
        /// Whether this application's audio is currently actively hooked and processed by Auralis 8D.
        /// </summary>
        public bool IsProcessing { get; set; }

        /// <summary>
        /// Peak audio level [0.0, 1.0] for real-time activity meter.
        /// </summary>
        public float AudioPeak { get; set; }

        /// <summary>
        /// Status message or error description for this session.
        /// </summary>
        public string StatusMessage { get; set; } = "Ready";
    }
}
