#define AURALIS8D_DSP_EXPORTS
#include "Auralis8D_DSP.h"
#include "DSPProcessor.h"

extern "C" {

    /**
     * Allocates and initializes a new DSPProcessor instance.
     */
    AURALIS8D_API void* Auralis8D_CreateDSP(uint32_t sampleRate) {
        try {
            return new Auralis8D::DSPProcessor(sampleRate);
        } catch (...) {
            return nullptr;
        }
    }

    /**
     * Deletes the DSPProcessor instance safely.
     */
    AURALIS8D_API void Auralis8D_DestroyDSP(void* handle) {
        if (handle) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            delete processor;
        }
    }

    /**
     * Updates the sample rate on the processor instance.
     */
    AURALIS8D_API void Auralis8D_SetSampleRate(void* handle, uint32_t sampleRate) {
        if (handle) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            processor->SetSampleRate(sampleRate);
        }
    }

    /**
     * Configures the target parameters on the processor instance.
     */
    AURALIS8D_API void Auralis8D_SetParameters(void* handle, float panSpeedHz, float panDepth, float stereoWidth, int bypass) {
        if (handle) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            Auralis8D::DSPParameters params;
            params.PanSpeedHz = panSpeedHz;
            params.PanDepth = panDepth;
            params.StereoWidth = stereoWidth;
            params.Bypass = (bypass != 0);
            processor->SetParameters(params);
        }
    }

    /**
     * Retrieves current parameters from the processor instance.
     */
    AURALIS8D_API void Auralis8D_GetParameters(void* handle, float* outPanSpeedHz, float* outPanDepth, float* outStereoWidth, int* outBypass) {
        if (handle) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            auto params = processor->GetParameters();
            if (outPanSpeedHz) *outPanSpeedHz = params.PanSpeedHz;
            if (outPanDepth) *outPanDepth = params.PanDepth;
            if (outStereoWidth) *outStereoWidth = params.StereoWidth;
            if (outBypass) *outBypass = params.Bypass ? 1 : 0;
        }
    }

    /**
     * Calls the real-time sample processing method for interleaved stereo data.
     */
    AURALIS8D_API void Auralis8D_ProcessInterleaved(void* handle, float* buffer, uint32_t frameCount) {
        if (handle && buffer && frameCount > 0) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            processor->ProcessInterleaved(buffer, frameCount);
        }
    }

    /**
     * Resets the processor internal states.
     */
    AURALIS8D_API void Auralis8D_Reset(void* handle) {
        if (handle) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            processor->Reset();
        }
    }

    /**
     * Retrieves the instantaneous pan position for UI animation.
     */
    AURALIS8D_API float Auralis8D_GetCurrentPanPosition(void* handle) {
        if (handle) {
            auto* processor = static_cast<Auralis8D::DSPProcessor*>(handle);
            return processor->GetCurrentPanPosition();
        }
        return 0.0f;
    }

}
