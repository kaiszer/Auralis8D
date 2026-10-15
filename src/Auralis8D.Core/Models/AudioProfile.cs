using System.Collections.Generic;

namespace Auralis8D.Core.Models
{
    /// <summary>
    /// Represents an 8D audio preset configuration profile.
    /// Provides predefined settings for rotation speed, depth, and stereo field expansion.
    /// </summary>
    public class AudioProfile
    {
        /// <summary>
        /// Display name of the profile.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Short description of the acoustic sensation delivered by this profile.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Stereo rotation speed in Hertz (cycles per second).
        /// </summary>
        public float SpeedHz { get; set; } = 0.15f;

        /// <summary>
        /// Depth of panning movement across channels (0.0 to 1.0).
        /// </summary>
        public float Depth { get; set; } = 1.0f;

        /// <summary>
        /// Mid/Side stereo width multiplier (0.0 to 2.0).
        /// </summary>
        public float StereoWidth { get; set; } = 1.0f;

        /// <summary>
        /// Flag indicating whether this is a predefined read-only default profile.
        /// </summary>
        public bool IsBuiltIn { get; set; } = true;

        /// <summary>
        /// Retrieves the collection of default system profiles.
        /// Predefined options are designed to provide varying levels of spatial immersion.
        /// </summary>
        public static IReadOnlyList<AudioProfile> GetDefaultProfiles()
        {
            return new List<AudioProfile>
            {
                new AudioProfile
                {
                    Name = "Gentle Rotation",
                    Description = "Subtle, relaxing motion with smooth transitions suitable for long listening sessions.",
                    SpeedHz = 0.08f,
                    Depth = 0.70f,
                    StereoWidth = 1.0f,
                    IsBuiltIn = true
                },
                new AudioProfile
                {
                    Name = "Balanced 8D",
                    Description = "Standard 8D audio sensation with full stereo sweep and natural width.",
                    SpeedHz = 0.15f,
                    Depth = 0.90f,
                    StereoWidth = 1.15f,
                    IsBuiltIn = true
                },
                new AudioProfile
                {
                    Name = "Immersive Rotation",
                    Description = "Dynamic, wide rotation with pronounced spatial presence and expanded soundstage.",
                    SpeedHz = 0.22f,
                    Depth = 1.0f,
                    StereoWidth = 1.35f,
                    IsBuiltIn = true
                },
                new AudioProfile
                {
                    Name = "Custom",
                    Description = "User-customized parameters.",
                    SpeedHz = 0.15f,
                    Depth = 0.90f,
                    StereoWidth = 1.15f,
                    IsBuiltIn = false
                }
            };
        }
    }
}
