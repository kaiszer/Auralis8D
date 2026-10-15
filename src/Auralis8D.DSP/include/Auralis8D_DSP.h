#pragma once

#include <cstdint>

#ifdef _WIN32
    #ifdef AURALIS8D_DSP_EXPORTS
        #define AURALIS8D_API __declspec(dllexport)
    #else
        #define AURALIS8D_API __declspec(dllimport)
    #endif
#else
    #define AURALIS8D_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

    /**
     * Creates a new instance of the 8D DSP audio processor.
     * @param sampleRate Initial audio sample rate in Hz (e.g., 44100 or 48000).
     * @return Opaque pointer to the DSP instance, or nullptr on failure.
     */
    AURALIS8D_API void* Auralis8D_CreateDSP(uint32_t sampleRate);

    /**
     * Destroys an existing DSP instance and frees associated resources.
     * @param handle Pointer returned by Auralis8D_CreateDSP.
     */
    AURALIS8D_API void Auralis8D_DestroyDSP(void* handle);

    /**
     * Updates the processing sample rate of the DSP instance.
     * @param handle Pointer to the DSP instance.
     * @param sampleRate New sample rate in Hz.
     */
    AURALIS8D_API void Auralis8D_SetSampleRate(void* handle, uint32_t sampleRate);

    /**
     * Sets target operational parameters for the 8D effect.
     * @param handle Pointer to the DSP instance.
     * @param panSpeedHz Panning speed in Hertz (cycles per second).
     * @param panDepth Panning intensity (0.0 to 1.0).
     * @param stereoWidth Stereo width scale factor (0.0 to 2.0).
     * @param bypass 1 to bypass the effect smoothly, 0 to enable.
     */
    AURALIS8D_API void Auralis8D_SetParameters(void* handle, float panSpeedHz, float panDepth, float stereoWidth, int bypass);

    /**
     * Retrieves current parameters from the DSP instance.
     * @param handle Pointer to the DSP instance.
     * @param outPanSpeedHz Pointer receiving the speed in Hz.
     * @param outPanDepth Pointer receiving the depth.
     * @param outStereoWidth Pointer receiving the stereo width.
     * @param outBypass Pointer receiving the bypass status (1 or 0).
     */
    AURALIS8D_API void Auralis8D_GetParameters(void* handle, float* outPanSpeedHz, float* outPanDepth, float* outStereoWidth, int* outBypass);

    /**
     * Processes an interleaved stereo float audio buffer in-place.
     * @param handle Pointer to the DSP instance.
     * @param buffer Pointer to interleaved 32-bit floating point audio data.
     * @param frameCount Number of stereo frames (each frame has 2 samples).
     */
    AURALIS8D_API void Auralis8D_ProcessInterleaved(void* handle, float* buffer, uint32_t frameCount);

    /**
     * Resets the internal phase and interpolation state.
     * @param handle Pointer to the DSP instance.
     */
    AURALIS8D_API void Auralis8D_Reset(void* handle);

    /**
     * Returns the instantaneous normalized pan position [-1.0, 1.0] for visualizers.
     * @param handle Pointer to the DSP instance.
     * @return Current pan position float.
     */
    AURALIS8D_API float Auralis8D_GetCurrentPanPosition(void* handle);

#ifdef __cplusplus
}
#endif
