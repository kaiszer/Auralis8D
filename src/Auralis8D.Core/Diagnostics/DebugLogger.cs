using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Auralis8D.Core.Diagnostics
{
    /// <summary>
    /// Severity level of diagnostic log messages.
    /// </summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Represents an individual structured diagnostic log entry.
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// Exact timestamp when the log entry was emitted.
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;

        /// <summary>
        /// Log severity level.
        /// </summary>
        public LogLevel Level { get; }

        /// <summary>
        /// Originating subsystem or class name.
        /// </summary>
        public string Component { get; }

        /// <summary>
        /// Informational or diagnostic description.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Optional stack trace or exception string.
        /// </summary>
        public string? ExceptionDetails { get; }

        /// <summary>
        /// Initializes a new diagnostic log entry instance.
        /// </summary>
        /// <param name="level">Severity level.</param>
        /// <param name="component">Component or module name.</param>
        /// <param name="message">Log message content.</param>
        /// <param name="exceptionDetails">Optional exception details.</param>
        public LogEntry(LogLevel level, string component, string message, string? exceptionDetails = null)
        {
            Level = level;
            Component = component;
            Message = message;
            ExceptionDetails = exceptionDetails;
        }

        /// <summary>
        /// Returns a standard formatted string for log export and clipboard operations.
        /// </summary>
        /// <returns>Formatted log line.</returns>
        public override string ToString()
        {
            string levelTag = Level switch
            {
                LogLevel.Warning => "[WARN]",
                LogLevel.Error => "[ERROR]",
                _ => "[INFO]"
            };

            string baseText = $"[{Timestamp:HH:mm:ss.fff}] {levelTag} [{Component}] {Message}";
            if (!string.IsNullOrEmpty(ExceptionDetails))
            {
                baseText += Environment.NewLine + "    " + ExceptionDetails;
            }
            return baseText;
        }
    }

    /// <summary>
    /// Thread-safe central diagnostic logging service.
    /// Maintains an in-memory rolling buffer for real-time UI inspection and persists
    /// all events to disk at %LOCALAPPDATA%\Auralis8D\diagnostics.log.
    /// </summary>
    public static class DebugLogger
    {
        private static readonly object _syncLock = new object();
        private static readonly List<LogEntry> _inMemoryBuffer = new List<LogEntry>();
        private const int MaxInMemoryEntries = 1000;
        private static readonly string _logFilePath;

        /// <summary>
        /// Event fired whenever a new log entry is added, enabling real-time streaming to the UI.
        /// </summary>
        public static event Action<LogEntry>? LogAdded;

        /// <summary>
        /// Absolute path to the diagnostic log file on disk.
        /// </summary>
        public static string LogFilePath => _logFilePath;

        /// <summary>
        /// Static constructor initializing file log storage path and header.
        /// </summary>
        static DebugLogger()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string folder = Path.Combine(localAppData, "Auralis8D");
                Directory.CreateDirectory(folder);
                _logFilePath = Path.Combine(folder, "diagnostics.log");

                // Initialize log file with session header
                string sessionHeader = $"=== AURALIS 8D DIAGNOSTICS SESSION STARTED AT {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"
                    + $"OS: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}){Environment.NewLine}"
                    + $".NET Runtime: {Environment.Version}{Environment.NewLine}"
                    + $"==================================================================={Environment.NewLine}";

                File.AppendAllText(_logFilePath, sessionHeader, Encoding.UTF8);
            }
            catch (Exception)
            {
                _logFilePath = Path.Combine(Path.GetTempPath(), "auralis8d_diagnostics.log");
            }
        }

        /// <summary>
        /// Records an informational log entry.
        /// </summary>
        /// <param name="component">Source component.</param>
        /// <param name="message">Log message.</param>
        public static void Log(string component, string message)
        {
            AddEntry(new LogEntry(LogLevel.Info, component, message));
        }

        /// <summary>
        /// Records a warning log entry.
        /// </summary>
        /// <param name="component">Source component.</param>
        /// <param name="message">Warning message.</param>
        public static void Warn(string component, string message)
        {
            AddEntry(new LogEntry(LogLevel.Warning, component, message));
        }

        /// <summary>
        /// Records an error log entry with optional exception details.
        /// </summary>
        /// <param name="component">Source component.</param>
        /// <param name="message">Error message.</param>
        /// <param name="ex">Optional associated exception.</param>
        public static void Error(string component, string message, Exception? ex = null)
        {
            string? details = ex != null ? $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}" : null;
            AddEntry(new LogEntry(LogLevel.Error, component, message, details));
        }

        /// <summary>
        /// Adds a log entry to the in-memory rolling buffer and appends it to disk.
        /// </summary>
        /// <param name="entry">Entry to persist and notify.</param>
        private static void AddEntry(LogEntry entry)
        {
            lock (_syncLock)
            {
                _inMemoryBuffer.Add(entry);
                if (_inMemoryBuffer.Count > MaxInMemoryEntries)
                {
                    _inMemoryBuffer.RemoveAt(0);
                }

                try
                {
                    File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception)
                {
                    // Fail silently on disk contention to preserve real-time audio threads
                }
            }

            try
            {
                LogAdded?.Invoke(entry);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Retrieves a snapshot copy of current in-memory log entries.
        /// </summary>
        /// <returns>List of recent log entries.</returns>
        public static List<LogEntry> GetRecentLogs()
        {
            lock (_syncLock)
            {
                return new List<LogEntry>(_inMemoryBuffer);
            }
        }

        /// <summary>
        /// Formats all current in-memory logs as a single contiguous string for clipboard export.
        /// </summary>
        /// <returns>Formatted multiline text of all diagnostic entries.</returns>
        public static string ExportLogsAsText()
        {
            lock (_syncLock)
            {
                var sb = new StringBuilder();
                foreach (var entry in _inMemoryBuffer)
                {
                    sb.AppendLine(entry.ToString());
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Clears the in-memory buffer.
        /// </summary>
        public static void Clear()
        {
            lock (_syncLock)
            {
                _inMemoryBuffer.Clear();
            }
        }
    }
}
