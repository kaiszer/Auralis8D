using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Auralis8D.Core.Diagnostics;

namespace Auralis8D.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Diagnostics &amp; Debug Log Viewer window.
    /// Exposes real-time streaming log entries and utilities for copying to clipboard or viewing on disk.
    /// </summary>
    public class DebugViewModel : ViewModelBase, IDisposable
    {
        private bool _autoScroll = true;
        private string _statusNotification = string.Empty;
        private bool _disposed;

        /// <summary>
        /// Observable collection of diagnostic entries bound to the log list view.
        /// </summary>
        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

        /// <summary>
        /// Controls whether the log viewer auto-scrolls to the newest entry.
        /// </summary>
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetField(ref _autoScroll, value);
        }

        /// <summary>
        /// Brief user feedback message (e.g., "Logs copied to clipboard").
        /// </summary>
        public string StatusNotification
        {
            get => _statusNotification;
            set => SetField(ref _statusNotification, value);
        }

        /// <summary>
        /// Path to the diagnostic log file on disk.
        /// </summary>
        public string LogFilePath => DebugLogger.LogFilePath;

        public ICommand CopyAllCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand OpenLogFolderCommand { get; }

        /// <summary>
        /// Initializes a new instance of the DebugViewModel and populates existing logs.
        /// </summary>
        public DebugViewModel()
        {
            CopyAllCommand = new RelayCommand(_ => CopyAllToClipboard());
            ClearCommand = new RelayCommand(_ => ClearLogs());
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

            // Populate existing log entries
            var initialLogs = DebugLogger.GetRecentLogs();
            foreach (var log in initialLogs)
            {
                Logs.Add(log);
            }

            // Subscribe to real-time log emissions
            DebugLogger.LogAdded += OnLogAdded;
        }

        /// <summary>
        /// Appends newly emitted log entries onto the UI collection using the dispatcher.
        /// </summary>
        /// <param name="entry">The emitted log entry.</param>
        private void OnLogAdded(LogEntry entry)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                Logs.Add(entry);
                if (Logs.Count > 1000)
                {
                    Logs.RemoveAt(0);
                }
            });
        }

        /// <summary>
        /// Copies all accumulated diagnostic logs to the Windows system clipboard.
        /// </summary>
        private void CopyAllToClipboard()
        {
            try
            {
                string text = DebugLogger.ExportLogsAsText();
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                    StatusNotification = "Copied all logs to clipboard.";
                }
                else
                {
                    StatusNotification = "No logs to copy.";
                }
            }
            catch (Exception ex)
            {
                StatusNotification = $"Clipboard copy error: {ex.Message}";
            }
        }

        /// <summary>
        /// Clears both in-memory log buffer and the UI view.
        /// </summary>
        private void ClearLogs()
        {
            DebugLogger.Clear();
            Logs.Clear();
            StatusNotification = "Diagnostics log cleared.";
        }

        /// <summary>
        /// Opens the Windows File Explorer highlighting the diagnostic log file.
        /// </summary>
        private void OpenLogFolder()
        {
            try
            {
                string path = DebugLogger.LogFilePath;
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                else
                {
                    string dir = Path.GetDirectoryName(path) ?? Path.GetTempPath();
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                StatusNotification = $"Failed to open folder: {ex.Message}";
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                DebugLogger.LogAdded -= OnLogAdded;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
