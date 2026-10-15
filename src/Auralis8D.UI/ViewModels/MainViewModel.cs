using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Auralis8D.Audio.Engine;
using Auralis8D.Audio.Output;
using Auralis8D.Core.Models;
using Auralis8D.Core.Services;

namespace Auralis8D.UI.ViewModels
{
    /// <summary>
    /// Master ViewModel driving the Auralis 8D desktop user interface.
    /// Manages audio engine state, effect parameters, profile selection, process discovery, and real-time visualizer updates.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly ConfigurationService _configService;
        private readonly ProfileManager _profileManager;
        private readonly AudioEngineCoordinator _engine;
        private readonly DispatcherTimer _visualizerTimer;
        private readonly DispatcherTimer _processScanTimer;

        private AudioSettings _settings;
        private bool _masterEnabled = true;
        private float _rotationSpeedHz = 0.15f;
        private float _effectDepth = 0.90f;
        private float _stereoWidth = 1.15f;
        private AudioProfile _selectedProfile;
        private string _statusText = "Audio engine ready.";
        private float _visualizerPan = 0.0f;
        private float _visualizerPeak = 0.0f;
        private bool _isAudioFlowing = false;
        private AudioDeviceItem? _selectedOutputDevice;
        private bool _isSettingsOpen = false;
        private bool _isAppManagerOpen = false;
        private bool _disposed;

        public ObservableCollection<AudioProfile> Profiles { get; } = new();
        public ObservableCollection<AppProcessViewModel> AvailableApps { get; } = new();
        public ObservableCollection<AppProcessViewModel> SelectedApps { get; } = new();
        public ObservableCollection<AudioDeviceItem> OutputDevices { get; } = new();

        public bool MasterEnabled
        {
            get => _masterEnabled;
            set
            {
                if (SetField(ref _masterEnabled, value))
                {
                    _settings.MasterEnabled = value;
                    ApplyEngineParameters();
                    OnPropertyChanged(nameof(MasterToggleText));
                    OnPropertyChanged(nameof(MasterButtonStatusText));
                    OnPropertyChanged(nameof(MasterButtonActionText));
                    OnPropertyChanged(nameof(StatusPillColor));
                    OnPropertyChanged(nameof(IsEffectActiveAndFlowing));
                }
            }
        }

        public string MasterToggleText => _masterEnabled ? "8D ACTIVE" : "BYPASS";
        public string MasterButtonStatusText => _masterEnabled ? "8D ROTATION: ACTIVE" : "8D ROTATION: BYPASSED";
        public string MasterButtonActionText => _masterEnabled ? "Click to bypass effect" : "Click to play / activate 8D effect";

        public float RotationSpeedHz
        {
            get => _rotationSpeedHz;
            set
            {
                if (SetField(ref _rotationSpeedHz, (float)Math.Round(value, 2)))
                {
                    _settings.RotationSpeedHz = _rotationSpeedHz;
                    OnParameterAdjustedManually();
                    ApplyEngineParameters();
                    OnPropertyChanged(nameof(SpeedDisplayText));
                }
            }
        }

        public string SpeedDisplayText => $"{_rotationSpeedHz:F2} Hz";

        public float EffectDepth
        {
            get => _effectDepth;
            set
            {
                if (SetField(ref _effectDepth, (float)Math.Round(value, 2)))
                {
                    _settings.EffectDepth = _effectDepth;
                    OnParameterAdjustedManually();
                    ApplyEngineParameters();
                    OnPropertyChanged(nameof(DepthDisplayText));
                }
            }
        }

        public string DepthDisplayText => $"{(int)(_effectDepth * 100)}%";

        public float StereoWidth
        {
            get => _stereoWidth;
            set
            {
                if (SetField(ref _stereoWidth, (float)Math.Round(value, 2)))
                {
                    _settings.StereoWidth = _stereoWidth;
                    OnParameterAdjustedManually();
                    ApplyEngineParameters();
                    OnPropertyChanged(nameof(WidthDisplayText));
                }
            }
        }

        public string WidthDisplayText => $"{_stereoWidth:F2}x";

        public AudioProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetField(ref _selectedProfile, value) && value != null)
                {
                    _settings.ActiveProfileName = value.Name;
                    if (value.Name != "Custom")
                    {
                        _rotationSpeedHz = value.SpeedHz;
                        _effectDepth = value.Depth;
                        _stereoWidth = value.StereoWidth;
                        OnPropertyChanged(nameof(RotationSpeedHz));
                        OnPropertyChanged(nameof(SpeedDisplayText));
                        OnPropertyChanged(nameof(EffectDepth));
                        OnPropertyChanged(nameof(DepthDisplayText));
                        OnPropertyChanged(nameof(StereoWidth));
                        OnPropertyChanged(nameof(WidthDisplayText));
                        ApplyEngineParameters();
                    }
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public float VisualizerPan
        {
            get => _visualizerPan;
            set => SetField(ref _visualizerPan, value);
        }

        public float VisualizerPeak
        {
            get => _visualizerPeak;
            set => SetField(ref _visualizerPeak, value);
        }

        public bool IsAudioFlowing
        {
            get => _isAudioFlowing;
            set
            {
                if (SetField(ref _isAudioFlowing, value))
                {
                    OnPropertyChanged(nameof(IsEffectActiveAndFlowing));
                    OnPropertyChanged(nameof(StatusPillColor));
                }
            }
        }

        public bool IsEffectActiveAndFlowing => _masterEnabled && _isAudioFlowing;

        public string StatusPillColor => IsEffectActiveAndFlowing ? "#06B6D4" : (_masterEnabled ? "#3B82F6" : "#64748B");

        public AudioDeviceItem? SelectedOutputDevice
        {
            get => _selectedOutputDevice;
            set
            {
                if (SetField(ref _selectedOutputDevice, value) && value != null)
                {
                    _settings.OutputDeviceId = value.Id;
                    _settings.OutputDeviceName = value.Name;
                    _engine.InitializeRenderer(value.Id);
                }
            }
        }

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => SetField(ref _isSettingsOpen, value);
        }

        public bool IsAppManagerOpen
        {
            get => _isAppManagerOpen;
            set => SetField(ref _isAppManagerOpen, value);
        }

        public bool HasSelectedApps => SelectedApps.Count > 0;
        public bool HasAvailableApps => AvailableApps.Count > 0;

        // Commands
        public RelayCommand ToggleMasterCommand { get; }
        public RelayCommand ResetControlsCommand { get; }
        public RelayCommand RefreshAppsCommand { get; }
        public RelayCommand ToggleSettingsCommand { get; }
        public RelayCommand ToggleAppManagerCommand { get; }
        public RelayCommand SelectProfileCommand { get; }

        public MainViewModel()
        {
            _configService = new ConfigurationService();
            _profileManager = new ProfileManager();
            _engine = new AudioEngineCoordinator();

            _settings = _configService.LoadSettings();

            // Populate profiles
            foreach (var prof in _profileManager.Profiles)
            {
                Profiles.Add(prof);
            }
            _selectedProfile = Profiles.FirstOrDefault(p => p.Name == _settings.ActiveProfileName) ?? Profiles.First();

            _masterEnabled = _settings.MasterEnabled;
            _rotationSpeedHz = _settings.RotationSpeedHz;
            _effectDepth = _settings.EffectDepth;
            _stereoWidth = _settings.StereoWidth;

            // Wire up commands
            ToggleMasterCommand = new RelayCommand(() => MasterEnabled = !MasterEnabled);
            ResetControlsCommand = new RelayCommand(ResetToDefaults);
            RefreshAppsCommand = new RelayCommand(RefreshProcessList);
            ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
            ToggleAppManagerCommand = new RelayCommand(() =>
            {
                IsAppManagerOpen = !IsAppManagerOpen;
                if (IsAppManagerOpen)
                {
                    RefreshProcessList();
                }
            });
            SelectProfileCommand = new RelayCommand(p =>
            {
                if (p is AudioProfile profile)
                {
                    SelectedProfile = profile;
                }
            });

            _engine.EngineStatusChanged = (status) =>
            {
                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = status;
                });
            };

            // Initialize audio renderer
            LoadOutputDevices();
            _engine.InitializeRenderer(_selectedOutputDevice?.Id ?? "");
            ApplyEngineParameters();

            // Setup real-time visualizer timer (60 FPS = 16ms)
            _visualizerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _visualizerTimer.Tick += OnVisualizerTick;
            _visualizerTimer.Start();

            // Setup background process scan timer (checks every 2 seconds)
            _processScanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _processScanTimer.Tick += (s, e) => RefreshProcessList();
            _processScanTimer.Start();

            RefreshProcessList();
        }

        private void LoadOutputDevices()
        {
            OutputDevices.Clear();
            var devices = DeviceEnumerator.GetAudioRenderDevices();
            foreach (var d in devices)
            {
                OutputDevices.Add(d);
            }

            if (!string.IsNullOrEmpty(_settings.OutputDeviceId))
            {
                _selectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == _settings.OutputDeviceId);
            }

            if (_selectedOutputDevice == null)
            {
                _selectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault) ?? OutputDevices.FirstOrDefault();
            }

            OnPropertyChanged(nameof(SelectedOutputDevice));
        }

        private void ApplyEngineParameters()
        {
            _engine.UpdateParameters(_masterEnabled, _rotationSpeedHz, _effectDepth, _stereoWidth);
            _configService.SaveSettings(_settings);
        }

        private void OnParameterAdjustedManually()
        {
            var custom = Profiles.FirstOrDefault(p => p.Name == "Custom");
            if (custom != null && _selectedProfile != custom)
            {
                _selectedProfile = custom;
                OnPropertyChanged(nameof(SelectedProfile));
            }
        }

        private void ResetToDefaults()
        {
            var balanced = Profiles.FirstOrDefault(p => p.Name == "Balanced 8D") ?? Profiles.First();
            SelectedProfile = balanced;
            MasterEnabled = true;
        }

        /// <summary>
        /// Scans active audio processes and synchronizes selections.
        /// </summary>
        public void RefreshProcessList()
        {
            var detected = _engine.ScanProcesses();

            // Update AvailableApps
            var currentPids = detected.Select(d => d.ProcessId).ToHashSet();

            // Remove dead processes
            for (int i = AvailableApps.Count - 1; i >= 0; i--)
            {
                if (!currentPids.Contains(AvailableApps[i].ProcessId))
                {
                    AvailableApps.RemoveAt(i);
                }
            }

            // Add or update
            foreach (var app in detected)
            {
                var existing = AvailableApps.FirstOrDefault(a => a.ProcessId == app.ProcessId);
                if (existing == null)
                {
                    var vm = new AppProcessViewModel(app);
                    // Check if this process was previously selected in user settings
                    if (_settings.RememberSelectedApps && _settings.SelectedProcessNames.Contains(app.ProcessName.ToLowerInvariant()))
                    {
                        vm.IsSelected = true;
                    }
                    vm.SelectionChanged = OnAppSelectionToggled;
                    AvailableApps.Add(vm);
                }
                else
                {
                    existing.IsActiveAudio = app.IsActiveAudio;
                    existing.AudioPeak = app.AudioPeak;
                    existing.IsProcessing = app.IsProcessing;
                    if (existing.SelectionChanged == null)
                    {
                        existing.SelectionChanged = OnAppSelectionToggled;
                    }
                }
            }

            OnPropertyChanged(nameof(HasAvailableApps));

            // Synchronize SelectedApps
            SyncSelectedApps();
        }

        public void OnAppSelectionToggled(AppProcessViewModel app)
        {
            string procKey = app.ProcessName.ToLowerInvariant();

            if (app.IsSelected)
            {
                if (!_settings.SelectedProcessNames.Contains(procKey))
                {
                    _settings.SelectedProcessNames.Add(procKey);
                }
            }
            else
            {
                _settings.SelectedProcessNames.Remove(procKey);
            }

            _configService.SaveSettings(_settings);
            SyncSelectedApps();
        }

        public void ToggleAppSelection(AppProcessViewModel app)
        {
            app.IsSelected = !app.IsSelected;
        }

        /// <summary>
        /// Synchronizes the SelectedApps collection with selected items from AvailableApps without wiping UI state.
        /// Notifies the audio engine coordinator of any process additions or removals.
        /// </summary>
        private void SyncSelectedApps()
        {
            var currentlySelected = AvailableApps.Where(a => a.IsSelected).ToList();
            var currentPids = currentlySelected.Select(a => a.ProcessId).ToHashSet();

            // Remove unselected applications
            for (int i = SelectedApps.Count - 1; i >= 0; i--)
            {
                if (!currentPids.Contains(SelectedApps[i].ProcessId))
                {
                    SelectedApps.RemoveAt(i);
                }
            }

            // Add newly selected applications
            foreach (var app in currentlySelected)
            {
                if (!SelectedApps.Any(s => s.ProcessId == app.ProcessId))
                {
                    SelectedApps.Add(app);
                }
            }

            OnPropertyChanged(nameof(HasSelectedApps));

            // Inform the engine of selected processes
            _engine.SyncSelectedProcesses(SelectedApps.Select(a => a.Model));
        }

        private void OnVisualizerTick(object? sender, EventArgs e)
        {
            VisualizerPan = _engine.CurrentPanPosition;
            VisualizerPeak = _engine.CurrentPeak;
            IsAudioFlowing = _engine.IsAudioFlowing;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _visualizerTimer.Stop();
                _processScanTimer.Stop();
                _engine.Dispose();
                _configService.SaveSettings(_settings);
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
