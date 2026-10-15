#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <audiopolicy.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <tlhelp32.h>
#include <iostream>
#include <string>
#include <vector>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "mmdevapi.lib")

using Microsoft::WRL::ComPtr;

// Helper to get process name from PID
std::wstring GetProcessName(DWORD pid) {
    if (pid == 0) return L"System Idle / System";
    HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnap == INVALID_HANDLE_VALUE) return L"Unknown";

    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    std::wstring name = L"Unknown";
    if (Process32FirstW(hSnap, &pe)) {
        do {
            if (pe.th32ProcessID == pid) {
                name = pe.szExeFile;
                break;
            }
        } while (Process32NextW(hSnap, &pe));
    }
    CloseHandle(hSnap);
    return name;
}

// Completion handler for ActivateAudioInterfaceAsync
class AudioInterfaceCompletionHandler :
    public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        Microsoft::WRL::FtmBase,
        IActivateAudioInterfaceCompletionHandler>
{
public:
    HANDLE CompletionEvent;
    HRESULT ActivationResult;
    ComPtr<IUnknown> ActivatedInterface;

    AudioInterfaceCompletionHandler()
        : CompletionEvent(CreateEvent(nullptr, FALSE, FALSE, nullptr)),
          ActivationResult(E_FAIL) {}

    ~AudioInterfaceCompletionHandler() {
        if (CompletionEvent) CloseHandle(CompletionEvent);
    }

    STDMETHODIMP ActivateCompleted(IActivateAudioInterfaceAsyncOperation* activateOperation) override {
        IUnknown* punkAudioInterface = nullptr;
        HRESULT hr = activateOperation->GetActivateResult(&ActivationResult, &punkAudioInterface);
        if (SUCCEEDED(hr) && SUCCEEDED(ActivationResult)) {
            ActivatedInterface.Attach(punkAudioInterface);
        }
        SetEvent(CompletionEvent);
        return S_OK;
    }
};

int main() {
    std::wcout << L"Auralis 8D - Testing Windows Application Loopback Capture Compatibility" << std::endl;
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) {
        std::wcerr << L"CoInitializeEx failed: " << std::hex << hr << std::endl;
        return 1;
    }

    ComPtr<IMMDeviceEnumerator> enumerator;
    hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
    if (FAILED(hr)) {
        std::wcerr << L"CoCreateInstance MMDeviceEnumerator failed: " << std::hex << hr << std::endl;
        CoUninitialize();
        return 1;
    }

    ComPtr<IMMDevice> defaultDevice;
    hr = enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &defaultDevice);
    if (FAILED(hr)) {
        std::wcerr << L"GetDefaultAudioEndpoint failed (No active audio output device?): " << std::hex << hr << std::endl;
        CoUninitialize();
        return 1;
    }

    // Enumerate sessions
    ComPtr<IAudioSessionManager2> sessionManager;
    hr = defaultDevice->Activate(__uuidof(IAudioSessionManager2), CLSCTX_ALL, nullptr, (void**)&sessionManager);
    if (SUCCEEDED(hr)) {
        ComPtr<IAudioSessionEnumerator> sessionEnum;
        hr = sessionManager->GetSessionEnumerator(&sessionEnum);
        if (SUCCEEDED(hr)) {
            int sessionCount = 0;
            sessionEnum->GetCount(&sessionCount);
            std::wcout << L"Found " << sessionCount << L" active audio sessions on default output device:" << std::endl;

            for (int i = 0; i < sessionCount; ++i) {
                ComPtr<IAudioSessionControl> control;
                if (SUCCEEDED(sessionEnum->GetSession(i, &control))) {
                    ComPtr<IAudioSessionControl2> control2;
                    if (SUCCEEDED(control.As(&control2))) {
                        DWORD pid = 0;
                        control2->GetProcessId(&pid);
                        std::wstring procName = GetProcessName(pid);
                        std::wcout << L"  - Session " << i << L": PID=" << pid << L" (" << procName << L")" << std::endl;
                    }
                }
            }
        }
    }

    // Test ActivateAudioInterfaceAsync on current process (or PID 4 / explorer)
    DWORD currentPid = GetCurrentProcessId();
    std::wcout << L"\nTesting Process Loopback activation on PID=" << currentPid << L"..." << std::endl;

    AUDIOCLIENT_ACTIVATION_PARAMS params = {};
    params.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    params.ProcessLoopbackParams.TargetProcessId = currentPid;
    params.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;

    PROPVARIANT activateParams = {};
    activateParams.vt = VT_BLOB;
    activateParams.blob.cbSize = sizeof(params);
    activateParams.blob.pBlobData = reinterpret_cast<BYTE*>(&params);

    ComPtr<AudioInterfaceCompletionHandler> handler = Microsoft::WRL::Make<AudioInterfaceCompletionHandler>();
    ComPtr<IActivateAudioInterfaceAsyncOperation> asyncOp;

    hr = ActivateAudioInterfaceAsync(
        VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
        __uuidof(IAudioClient),
        &activateParams,
        handler.Get(),
        &asyncOp
    );

    if (FAILED(hr)) {
        std::wcerr << L"ActivateAudioInterfaceAsync call failed with HRESULT: 0x" << std::hex << hr << std::endl;
    } else {
        std::wcout << L"ActivateAudioInterfaceAsync dispatched. Waiting for completion..." << std::endl;
        DWORD waitRes = WaitForSingleObject(handler->CompletionEvent, 3000);
        if (waitRes == WAIT_OBJECT_0) {
            std::wcout << L"Async activation completed with result HRESULT: 0x" << std::hex << handler->ActivationResult << std::endl;
            if (SUCCEEDED(handler->ActivationResult) && handler->ActivatedInterface) {
                std::wcout << L"SUCCESS! Process Loopback IAudioClient successfully activated!" << std::endl;
                ComPtr<IAudioClient> client;
                if (SUCCEEDED(handler->ActivatedInterface.As(&client))) {
                    WAVEFORMATEX* pwfx = nullptr;
                    if (SUCCEEDED(client->GetMixFormat(&pwfx))) {
                        std::wcout << L"Mix format: " << pwfx->nSamplesPerSec << L" Hz, "
                                   << pwfx->nChannels << L" channels, "
                                   << pwfx->wBitsPerSample << L" bits." << std::endl;
                        CoTaskMemFree(pwfx);
                    }
                }
            }
        } else {
            std::wcerr << L"Timed out waiting for completion event." << std::endl;
        }
    }

    CoUninitialize();
    std::wcout << L"Verification complete." << std::endl;
    return 0;
}
