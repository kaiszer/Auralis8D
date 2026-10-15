using System;
using System.Collections.Generic;
using System.Linq;
using Auralis8D.Core.Models;

namespace Auralis8D.Core.Services
{
    /// <summary>
    /// Manages the catalog of 8D audio profiles and handles active profile selection and customization.
    /// </summary>
    public class ProfileManager
    {
        private readonly List<AudioProfile> _profiles;

        /// <summary>
        /// Gets the current list of available audio profiles.
        /// </summary>
        public IReadOnlyList<AudioProfile> Profiles => _profiles;

        /// <summary>
        /// Currently active profile.
        /// </summary>
        public AudioProfile ActiveProfile { get; private set; }

        /// <summary>
        /// Initializes the profile manager with system default profiles.
        /// </summary>
        public ProfileManager()
        {
            _profiles = AudioProfile.GetDefaultProfiles().ToList();
            ActiveProfile = _profiles.First(p => p.Name == "Balanced 8D");
        }

        /// <summary>
        /// Selects an active profile by name. If not found, falls back to Balanced 8D.
        /// </summary>
        /// <param name="profileName">Name of the profile to activate.</param>
        /// <returns>The newly active AudioProfile.</returns>
        public AudioProfile SelectProfile(string profileName)
        {
            var match = _profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ActiveProfile = match;
            }
            else
            {
                ActiveProfile = _profiles.First();
            }
            return ActiveProfile;
        }

        /// <summary>
        /// Updates custom profile parameters when the user modifies slider values directly.
        /// </summary>
        /// <param name="speedHz">Rotation speed in Hz.</param>
        /// <param name="depth">Panning depth [0.0, 1.0].</param>
        /// <param name="stereoWidth">Stereo width multiplier [0.0, 2.0].</param>
        public void UpdateCustomProfile(float speedHz, float depth, float stereoWidth)
        {
            var custom = _profiles.FirstOrDefault(p => p.Name == "Custom");
            if (custom != null)
            {
                custom.SpeedHz = speedHz;
                custom.Depth = depth;
                custom.StereoWidth = stereoWidth;
                ActiveProfile = custom;
            }
        }
    }
}
