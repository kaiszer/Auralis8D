#pragma once

#include <cmath>
#include <cstdint>
#include <algorithm>

namespace Auralis8D {

    /**
     * Configuration parameters for the 8D stereo rotation audio effect.
     */
    struct DSPParameters {
        float PanSpeedHz;       // Rotation frequency in Hz (e.g. 0.15 Hz for ~6.6s full cycle)
        float PanDepth;         // Intensity of stereo panning (0.0 = center only, 1.0 = full left to right)
        float StereoWidth;      // Mid/Side stereo width multiplier (0.0 = mono, 1.0 = normal stereo, 2.0 = ultra-wide)
        bool  Bypass;           // When true, the effect is smoothly bypassed without audio interruption
    };

    /**
     * Real-time 8D audio digital signal processor.
     * Implements sample-accurate sinusoidal equal-power panning,
     * mid/side stereo expansion, continuous phase tracking,
     * smooth parameter interpolation to avoid clicks, and soft saturation clipping prevention.
     */
    class DSPProcessor {
    public:
        /**
         * Constructor. Initializes DSP internal state and default parameters.
         * @param sampleRate The audio sample rate in Hz (e.g., 44100, 48000).
         */
        explicit DSPProcessor(uint32_t sampleRate = 48000);

        /**
         * Destructor.
         */
        ~DSPProcessor() = default;

        /**
         * Updates the audio sample rate.
         * Recalculates phase increment while maintaining current phase position.
         * @param sampleRate New sample rate in Hz.
         */
        void SetSampleRate(uint32_t sampleRate);

        /**
         * Sets target parameters for smooth interpolation.
         * @param params Target parameters.
         */
        void SetParameters(const DSPParameters& params);

        /**
         * Retrieves current target parameters.
         * @return Current parameters struct.
         */
        DSPParameters GetParameters() const;

        /**
         * Processes an interleaved stereo 32-bit floating point audio buffer in-place.
         * Interleaved layout: [L0, R0, L1, R1, ...].
         * @param buffer Pointer to interleaved float audio buffer.
         * @param frameCount Number of stereo frames (each frame is 2 float samples).
         */
        void ProcessInterleaved(float* buffer, size_t frameCount);

        /**
         * Resets phase accumulator and smoothing state to initial values.
         */
        void Reset();

        /**
         * Returns the current normalized pan position [-1.0 (left), 0.0 (center), +1.0 (right)]
         * for UI visualization purposes.
         */
        float GetCurrentPanPosition() const;

    private:
        /**
         * Applies cubic / hyperbolic-tangent soft saturation curve to prevent hard digital clipping.
         * @param sample Input float sample.
         * @return Saturated sample strictly bounded within [-1.0, 1.0].
         */
        static inline float SoftSaturate(float sample) {
            const float threshold = 0.85f;
            if (sample > threshold) {
                float excess = sample - threshold;
                return threshold + (1.0f - threshold) * std::tanh(excess / (1.0f - threshold));
            } else if (sample < -threshold) {
                float excess = -sample - threshold;
                return -(threshold + (1.0f - threshold) * std::tanh(excess / (1.0f - threshold)));
            }
            return sample;
        }

        uint32_t m_sampleRate;
        DSPParameters m_targetParams;

        // Current smoothed parameters to prevent zipper noise during slider movement
        float m_currentSpeed;
        float m_currentDepth;
        float m_currentWidth;
        float m_currentBypassFade; // 0.0 = fully active (wet), 1.0 = fully bypassed (dry)

        // Continuous phase accumulator in range [0, 2 * PI)
        double m_phase;

        // Cached current normalized pan value for UI visualizer
        float m_currentPanPosition;
    };

} // namespace Auralis8D
