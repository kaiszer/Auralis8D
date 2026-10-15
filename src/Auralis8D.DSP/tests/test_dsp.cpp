#include <iostream>
#include <vector>
#include <cmath>
#include <cassert>
#include "../include/DSPProcessor.h"

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

// Helper function to test floating point equality within tolerance
static bool ApproxEqual(float a, float b, float epsilon = 0.001f) {
    return std::fabs(a - b) <= epsilon;
}

/**
 * Test 1: Equal-power panning law preserves total energy.
 * When a constant mono signal is panned across the stereo field,
 * L^2 + R^2 should equal input power.
 */
void TestEqualPowerPanning() {
    std::cout << "[TEST] Running TestEqualPowerPanning..." << std::endl;
    const uint32_t sampleRate = 48000;
    Auralis8D::DSPProcessor dsp(sampleRate);

    Auralis8D::DSPParameters params;
    params.PanSpeedHz = 0.5f; // Faster rotation to cycle through
    params.PanDepth = 1.0f;
    params.StereoWidth = 1.0f;
    params.Bypass = false;
    dsp.SetParameters(params);

    // Warm up smoothing
    std::vector<float> warmup(sampleRate * 2, 0.5f);
    dsp.ProcessInterleaved(warmup.data(), sampleRate);

    // Test a block of constant-amplitude frames
    const size_t testFrames = 4800; // 0.1 seconds
    std::vector<float> buffer(testFrames * 2, 0.5f); // Stereo DC signal of 0.5

    dsp.ProcessInterleaved(buffer.data(), testFrames);

    for (size_t i = 0; i < testFrames; ++i) {
        float l = buffer[i * 2];
        float r = buffer[i * 2 + 1];
        // Calculate power: L^2 + R^2
        float power = (l * l) + (r * r);
        // Input had L=0.5, R=0.5. With width=1.0 and equal-power law:
        // L_gain^2 + R_gain^2 = 1.0 => (0.5 * gainL)^2 + (0.5 * gainR)^2 = 0.25 * (gainL^2 + gainR^2) = 0.25
        assert(ApproxEqual(power, 0.25f, 0.05f));
    }
    std::cout << "[PASS] TestEqualPowerPanning passed successfully." << std::endl;
}

/**
 * Test 2: Phase continuity across buffer boundaries.
 * Processing 10 small chunks vs 1 continuous buffer should yield identical results.
 */
void TestPhaseContinuity() {
    std::cout << "[TEST] Running TestPhaseContinuity..." << std::endl;
    const uint32_t sampleRate = 48000;
    const size_t totalFrames = 2048;

    // Generate input sine wave
    std::vector<float> inputSignal(totalFrames * 2);
    for (size_t i = 0; i < totalFrames; ++i) {
        float sample = std::sin(2.0 * M_PI * 440.0 * i / sampleRate) * 0.3f;
        inputSignal[i * 2] = sample;
        inputSignal[i * 2 + 1] = sample;
    }

    // 1. Process as one single buffer
    Auralis8D::DSPProcessor dspSingle(sampleRate);
    Auralis8D::DSPParameters params{ 0.2f, 1.0f, 1.0f, false };
    dspSingle.SetParameters(params);
    dspSingle.Reset();

    std::vector<float> singleOutput = inputSignal;
    dspSingle.ProcessInterleaved(singleOutput.data(), totalFrames);

    // 2. Process in small chunks (e.g., 256 frames each)
    Auralis8D::DSPProcessor dspChunked(sampleRate);
    dspChunked.SetParameters(params);
    dspChunked.Reset();

    std::vector<float> chunkedOutput = inputSignal;
    const size_t chunkSize = 256;
    for (size_t offset = 0; offset < totalFrames; offset += chunkSize) {
        size_t framesToProcess = std::min(chunkSize, totalFrames - offset);
        dspChunked.ProcessInterleaved(chunkedOutput.data() + (offset * 2), framesToProcess);
    }

    // Verify chunked matches single processing
    for (size_t i = 0; i < totalFrames * 2; ++i) {
        assert(ApproxEqual(singleOutput[i], chunkedOutput[i], 0.0001f));
    }
    std::cout << "[PASS] TestPhaseContinuity passed successfully." << std::endl;
}

/**
 * Test 3: Soft Saturation prevents clipping beyond [-1.0, 1.0].
 */
void TestSoftSaturation() {
    std::cout << "[TEST] Running TestSoftSaturation..." << std::endl;
    const uint32_t sampleRate = 48000;
    Auralis8D::DSPProcessor dsp(sampleRate);
    dsp.Reset();

    const size_t frames = 512;
    std::vector<float> buffer(frames * 2);
    // Fill with extreme hot signal (+2.5 to -2.5)
    for (size_t i = 0; i < frames; ++i) {
        float hotVal = 2.5f * std::sin(2.0 * M_PI * 100.0 * i / sampleRate);
        buffer[i * 2] = hotVal;
        buffer[i * 2 + 1] = hotVal;
    }

    dsp.ProcessInterleaved(buffer.data(), frames);

    for (size_t i = 0; i < frames * 2; ++i) {
        // Must be strictly within [-1.0, 1.0]
        assert(buffer[i] <= 1.0f && buffer[i] >= -1.0f);
    }
    std::cout << "[PASS] TestSoftSaturation passed successfully." << std::endl;
}

/**
 * Test 4: Bypass mode restores dry audio smoothly.
 */
void TestBypassMode() {
    std::cout << "[TEST] Running TestBypassMode..." << std::endl;
    const uint32_t sampleRate = 48000;
    Auralis8D::DSPProcessor dsp(sampleRate);

    Auralis8D::DSPParameters params{ 0.2f, 1.0f, 1.0f, true }; // Bypass = true
    dsp.SetParameters(params);
    dsp.Reset();

    // Warm up bypass transition
    const size_t warmupFrames = 4800;
    std::vector<float> warmup(warmupFrames * 2, 0.4f);
    dsp.ProcessInterleaved(warmup.data(), warmupFrames);

    // Test audio
    const size_t testFrames = 512;
    std::vector<float> buffer(testFrames * 2);
    for (size_t i = 0; i < testFrames; ++i) {
        buffer[i * 2] = 0.35f;
        buffer[i * 2 + 1] = 0.65f;
    }

    dsp.ProcessInterleaved(buffer.data(), testFrames);

    for (size_t i = 0; i < testFrames; ++i) {
        assert(ApproxEqual(buffer[i * 2], 0.35f, 0.01f));
        assert(ApproxEqual(buffer[i * 2 + 1], 0.65f, 0.01f));
    }
    std::cout << "[PASS] TestBypassMode passed successfully." << std::endl;
}

int main() {
    std::cout << "Starting DSP Algorithm Tests..." << std::endl;
    try {
        TestEqualPowerPanning();
        TestPhaseContinuity();
        TestSoftSaturation();
        TestBypassMode();
        std::cout << "All DSP Unit Tests Passed!" << std::endl;
        return 0;
    } catch (const std::exception& ex) {
        std::cerr << "Test failure: " << ex.what() << std::endl;
        return 1;
    }
}
