#include "DSPProcessor.h"
#include <cmath>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace Auralis8D {

    /**
     * Initializes the processor with a given sample rate and standard default settings.
     * Default pan speed is 0.15 Hz (~6.67 seconds per rotation cycle).
     * Default pan depth is 1.0 (full left-to-right sweep).
     * Default stereo width is 1.0 (natural stereo preservation).
     */
    DSPProcessor::DSPProcessor(uint32_t sampleRate)
        : m_sampleRate(sampleRate > 0 ? sampleRate : 48000),
          m_currentSpeed(0.15f),
          m_currentDepth(1.0f),
          m_currentWidth(1.0f),
          m_currentBypassFade(0.0f),
          m_phase(0.0),
          m_currentPanPosition(0.0f)
    {
        m_targetParams.PanSpeedHz = 0.15f;
        m_targetParams.PanDepth = 1.0f;
        m_targetParams.StereoWidth = 1.0f;
        m_targetParams.Bypass = false;
    }

    /**
     * Updates the sample rate without resetting the current phase position.
     * Preserving phase across sample rate changes prevents audible clicks.
     */
    void DSPProcessor::SetSampleRate(uint32_t sampleRate) {
        if (sampleRate > 0) {
            m_sampleRate = sampleRate;
        }
    }

    /**
     * Updates the target parameters for the DSP processing loop.
     * The processing loop smoothly interpolates toward these target values.
     */
    void DSPProcessor::SetParameters(const DSPParameters& params) {
        // Clamp parameters to safe operating limits
        m_targetParams.PanSpeedHz = std::clamp(params.PanSpeedHz, 0.01f, 5.0f);
        m_targetParams.PanDepth = std::clamp(params.PanDepth, 0.0f, 1.0f);
        m_targetParams.StereoWidth = std::clamp(params.StereoWidth, 0.0f, 2.0f);
        m_targetParams.Bypass = params.Bypass;
    }

    /**
     * Returns the current target parameters.
     */
    DSPParameters DSPProcessor::GetParameters() const {
        return m_targetParams;
    }

    /**
     * Resets internal oscillator phase and smoothing states to baseline.
     */
    void DSPProcessor::Reset() {
        m_phase = 0.0;
        m_currentSpeed = m_targetParams.PanSpeedHz;
        m_currentDepth = m_targetParams.PanDepth;
        m_currentWidth = m_targetParams.StereoWidth;
        m_currentBypassFade = m_targetParams.Bypass ? 1.0f : 0.0f;
        m_currentPanPosition = 0.0f;
    }

    /**
     * Returns the instantaneous pan position for UI meter visualization.
     * -1.0 represents far left, 0.0 represents center, +1.0 represents far right.
     * In bypass mode, returns 0.0 (center) to indicate inactive spatial rotation.
     */
    float DSPProcessor::GetCurrentPanPosition() const {
        return m_targetParams.Bypass ? 0.0f : m_currentPanPosition;
    }

    /**
     * Real-time sample processing engine.
     * Operates in-place on 32-bit floating point interleaved stereo audio.
     * This function is strictly zero-allocation and lock-free to satisfy real-time audio constraints.
     */
    void DSPProcessor::ProcessInterleaved(float* buffer, size_t frameCount) {
        if (!buffer || frameCount == 0 || m_sampleRate == 0) {
            return;
        }

        const double twoPi = 2.0 * M_PI;
        const double halfPi = M_PI * 0.5;

        // Exponential smoothing coefficient for 20ms transition time constant
        // This ensures slider changes never cause zipper noise or pops.
        const float smoothAlpha = static_cast<float>(1.0 - std::exp(-1.0 / (m_sampleRate * 0.020)));

        const float targetBypass = m_targetParams.Bypass ? 1.0f : 0.0f;

        for (size_t i = 0; i < frameCount; ++i) {
            // 1. Smoothly update parameter values per sample
            m_currentSpeed += smoothAlpha * (m_targetParams.PanSpeedHz - m_currentSpeed);
            m_currentDepth += smoothAlpha * (m_targetParams.PanDepth - m_currentDepth);
            m_currentWidth += smoothAlpha * (m_targetParams.StereoWidth - m_currentWidth);
            m_currentBypassFade += smoothAlpha * (targetBypass - m_currentBypassFade);

            // 2. Read original input samples
            size_t leftIndex = i * 2;
            size_t rightIndex = leftIndex + 1;
            float inLeft = buffer[leftIndex];
            float inRight = buffer[rightIndex];

            // 3. Mid/Side Stereo Width processing
            // Preserves source audio stereo content while allowing controlled width expansion
            float mid = 0.5f * (inLeft + inRight);
            float side = 0.5f * (inLeft - inRight);
            float wideSide = side * m_currentWidth;

            float wideLeft = mid + wideSide;
            float wideRight = mid - wideSide;

            // 4. Continuous phase update for the sinusoidal LFO
            double phaseIncrement = (twoPi * static_cast<double>(m_currentSpeed)) / static_cast<double>(m_sampleRate);
            m_phase += phaseIncrement;
            if (m_phase >= twoPi) {
                m_phase -= twoPi;
            }

            // 5. Calculate sinusoidal pan value in range [-1.0, 1.0]
            float rawPan = static_cast<float>(std::sin(m_phase)) * m_currentDepth;
            m_currentPanPosition = m_targetParams.Bypass ? 0.0f : rawPan;

            // 6. Map pan value to [0.0, 1.0] where 0.0 is left, 0.5 is center, 1.0 is right
            float panPosition = (rawPan + 1.0f) * 0.5f;

            // 7. Constant-power panning law (Equal-Power Panning)
            // Using cos(angle) for left and sin(angle) for right guarantees
            // that (cos(angle)^2 + sin(angle)^2 == 1), preventing any volume drop at center.
            float angle = panPosition * static_cast<float>(halfPi);
            float gainLeft = std::cos(angle);
            float gainRight = std::sin(angle);

            // 8. Apply 8D panning gains
            float wetLeft = wideLeft * gainLeft;
            float wetRight = wideRight * gainRight;

            // 9. Smooth crossfade between processed audio and original dry audio
            // Ensures clickless transition when bypassing or enabling the effect
            float dryWeight = m_currentBypassFade;
            float wetWeight = 1.0f - dryWeight;

            float outLeft = (wetLeft * wetWeight) + (inLeft * dryWeight);
            float outRight = (wetRight * wetWeight) + (inRight * dryWeight);

            // 10. Soft saturation limiter to prevent digital clipping
            // Smoothly compresses signals that approach or exceed full scale (+-1.0)
            buffer[leftIndex] = SoftSaturate(outLeft);
            buffer[rightIndex] = SoftSaturate(outRight);
        }
    }

} // namespace Auralis8D
