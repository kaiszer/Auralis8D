using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Auralis8D.Core.Models;

namespace Auralis8D.UI.ViewModels
{
    /// <summary>
    /// ViewModel representing a single audio-producing process with icon extraction, selection toggle, and activity status.
    /// </summary>
    public class AppProcessViewModel : ViewModelBase
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;

        private readonly AppProcessItem _model;
        private ImageSource? _iconSource;

        public AppProcessItem Model => _model;

        public int ProcessId => _model.ProcessId;
        public string ProcessName => _model.ProcessName;
        public string DisplayName => _model.DisplayName;
        public string ExecutablePath => _model.ExecutablePath;

        public bool IsSelected
        {
            get => _model.IsSelected;
            set
            {
                if (_model.IsSelected != value)
                {
                    _model.IsSelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        public bool IsActiveAudio
        {
            get => _model.IsActiveAudio;
            set
            {
                if (_model.IsActiveAudio != value)
                {
                    _model.IsActiveAudio = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        public bool IsProcessing
        {
            get => _model.IsProcessing;
            set
            {
                if (_model.IsProcessing != value)
                {
                    _model.IsProcessing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        public float AudioPeak
        {
            get => _model.AudioPeak;
            set
            {
                if (Math.Abs(_model.AudioPeak - value) > 0.01f)
                {
                    _model.AudioPeak = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusDisplay
        {
            get
            {
                if (_model.IsProcessing) return "8D Active";
                if (_model.IsSelected) return "Selected (Waiting for Audio)";
                if (_model.IsActiveAudio) return "Audio Playing";
                return "Inactive";
            }
        }

        public ImageSource? IconSource
        {
            get
            {
                if (_iconSource == null && !string.IsNullOrEmpty(_model.ExecutablePath))
                {
                    _iconSource = ExtractIcon(_model.ExecutablePath);
                }
                return _iconSource;
            }
        }

        public AppProcessViewModel(AppProcessItem model)
        {
            _model = model;
        }

        /// <summary>
        /// Extracts small application icon directly from disk using native Windows Shell API.
        /// </summary>
        private static ImageSource? ExtractIcon(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON);
                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions()
                    );
                    source.Freeze();
                    DestroyIcon(shinfo.hIcon);
                    return source;
                }
            }
            catch (Exception)
            {
                // Fallback gracefully to default icon
            }

            return null;
        }
    }
}
