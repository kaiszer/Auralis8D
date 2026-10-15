using System;
using System.IO;
using System.Text.Json;
using Auralis8D.Core.Models;

namespace Auralis8D.Core.Services
{
    /// <summary>
    /// Service responsible for loading, saving, and managing application settings and user preferences.
    /// Operates completely offline, persisting JSON configuration to the user's LocalAppData folder.
    /// </summary>
    public class ConfigurationService
    {
        private readonly string _configFilePath;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Initializes a new instance of ConfigurationService.
        /// Configures local storage path under %LOCALAPPDATA%/Auralis8D/settings.json.
        /// </summary>
        public ConfigurationService()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(localAppData, "Auralis8D");
            if (!Directory.Exists(appDir))
            {
                Directory.CreateDirectory(appDir);
            }
            _configFilePath = Path.Combine(appDir, "settings.json");
        }

        /// <summary>
        /// Constructor allowing custom config file path (useful for unit testing).
        /// </summary>
        /// <param name="customPath">Explicit file path for settings JSON.</param>
        public ConfigurationService(string customPath)
        {
            _configFilePath = customPath;
            string? dir = Path.GetDirectoryName(customPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// Loads settings from disk. Returns default settings if file does not exist or fails to parse.
        /// </summary>
        /// <returns>AudioSettings instance populated from disk or default.</returns>
        public AudioSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var settings = JsonSerializer.Deserialize<AudioSettings>(json, _jsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
                // Silently fallback to defaults if config file is corrupted
            }

            var defaultSettings = new AudioSettings();
            SaveSettings(defaultSettings);
            return defaultSettings;
        }

        /// <summary>
        /// Persists settings to disk safely via atomic file write.
        /// </summary>
        /// <param name="settings">The settings object to serialize.</param>
        public void SaveSettings(AudioSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                string tempPath = _configFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _configFilePath, true);
            }
            catch (Exception)
            {
                // Ignore transient write errors during shutdown
            }
        }
    }
}
