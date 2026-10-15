# Auralis 8D — Real-Time Windows Audio Enhancer

Auralis 8D is a native Windows desktop application that applies real-time 8D stereo rotation and spatial enhancement to audio from user-selected applications (such as Spotify, Google Chrome, Microsoft Edge, media players, and video games).

Unlike traditional file converters, Auralis 8D processes live streaming audio continuously without requiring users to import, convert, or export files. All unselected Windows audio streams remain untouched.

---

## Architecture Overview

```
                                +---------------------------+
                                |  Target Audio Application | (e.g. Spotify, Chrome, Edge)
                                +-------------+-------------+
                                              |
                                              v (WASAPI Process Loopback Capture)
                                +-------------+-------------+
                                |     Auralis8D.Audio       |  [Mutes original direct session]
                                +-------------+-------------+
                                              |
                                              v (Lock-Free Interleaved Float32 Buffers)
                                +-------------+-------------+
                                |      Auralis8D.DSP        |  (Native C++ Real-Time DLL)
                                | - Equal-Power Panning Law |
                                | - Continuous Phase LFO    |
                                | - Mid/Side Stereo Width   |
                                | - Soft-Saturation Limiter |
                                +-------------+-------------+
                                              |
                                              v (Ring Buffer Decoupling)
                                +-------------+-------------+
                                |     WASAPI Renderer       |  (Output Endpoint: Headphones/Speakers)
                                +---------------------------+
```

### Component Breakdown
1. **Auralis8D.DSP** (Native C++ x64):
   - Real-time stereo processor with continuous phase accumulator ($\theta \in [0, 2\pi)$) avoiding phase discontinuities.
   - Equal-power sinusoidal panning law ($\cos(\alpha)$ for Left, $\sin(\alpha)$ for Right) preserving total acoustic energy ($L^2 + R^2 = 1$).
   - Mid/Side stereo width processing ($M = 0.5(L+R), S = 0.5(L-R) \cdot \text{width}$).
   - Smooth parameter interpolation using exponential smoothing ($\tau \approx 20\text{ ms}$) eliminating zipper noise and clicks.
   - Smooth bypass crossfade and cubic/hyperbolic soft saturation anti-clipping limiter.
   - C ABI exports (`Auralis8D_CreateDSP`, `Auralis8D_ProcessInterleaved`, etc.).

2. **Auralis8D.Audio** (C# .NET 8 / Core Audio):
   - Integrates Windows Application Loopback Capture via `ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK)`.
   - Mutes the target application's original session volume on the default render endpoint to prevent acoustic echo or duplicate audio.
   - Restores original session mute state immediately upon engine stop or error.
   - Decoupled playback streaming via lock-free circular ring buffer to WASAPI event-driven shared render client.
   - Discovers and tracks active audio sessions using `IAudioSessionManager2` and `IAudioMeterInformation`.

3. **Auralis8D.Core** (C# .NET 8):
   - Audio profile management ("Gentle Rotation", "Balanced 8D", "Immersive Rotation", "Custom").
   - Offline JSON persistence for user preferences, application selections, and audio devices (`%LOCALAPPDATA%\Auralis8D\settings.json`).

4. **Auralis8D.UI** (C# .NET 8 WPF):
   - Modern, understated Windows desktop interface styled in deep navy blue (`#0B1120`, `#0F172A`), medium blue (`#2563EB`), subtle cyan (`#06B6D4`), cool gray and silver (`#94A3B8`, `#334155`), and crisp white text.
   - Real-time `StereoVisualizer` control displaying live spatial trajectory without CPU overhead.
   - Application management modal for selective application filtering.
   - Settings dialog for audio output device selection and startup options.

5. **Auralis8D.Tests** (C# .NET 8):
   - Automated test suite validating DSP phase continuity across buffers, equal-power conservation, soft saturation limits, Mid/Side stereo widening, JSON persistence, and session discovery.

---

## Requirements

### Operating System & SDK
- Windows 11 or Windows 10 (Build 20348 or newer).
- Windows SDK 10.0.22621.0 or newer (provides `AUDIOCLIENT_ACTIVATION_PARAMS` and `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`).

### Development & Build Tools
- .NET 8.0 SDK (or .NET 10 preview)
- Microsoft Visual C++ MSVC v143 (Visual Studio 2022 Community / Build Tools)

---

## Build and Run Instructions

### 1. Build the Native C++ DSP Engine
Run the build script from the project root:
```cmd
build_dsp.bat
```
This builds `src\Auralis8D.DSP\Auralis8D.DSP.dll` (x64 Release).

### 2. Build the Complete Solution
Build the entire solution using the .NET CLI:
```cmd
dotnet build Auralis8D.sln -c Release
```

### 3. Run the Automated Tests
Execute the automated test suite:
```cmd
dotnet run --project src\Auralis8D.Tests\Auralis8D.Tests.csproj -c Release
```
Expected output:
```
Summary: 10 passed, 0 failed.
```

### 4. Run the Application
Launch Auralis 8D:
```cmd
dotnet run --project src\Auralis8D.UI\Auralis8D.UI.csproj -c Release
```
Or directly run the compiled binary:
```cmd
src\Auralis8D.UI\bin\x64\Release\net8.0-windows\Auralis8D.exe
```

---

## Packaging Instructions

To create a standalone distribution folder:
```cmd
dotnet publish src\Auralis8D.UI\Auralis8D.UI.csproj -c Release -r win-x64 --self-contained false -o dist
```
The output `dist\` directory will contain `Auralis8D.exe`, all required managed assemblies, and `Auralis8D.DSP.dll`.

---

## Known Limitations & Considerations
- **Process Loopback OS Requirement**: Windows Application Loopback Capture requires Windows 10 build 20348+ or Windows 11. On older Windows 10 versions, the OS does not support process-isolated loopback without custom audio drivers.
- **Protected Audio (DRM)**: Applications protected with Windows Protected Media Path (PMP) or exclusive mode audio cannot be captured via shared loopback.
- **Multi-Process Browsers**: Modern web browsers (Chrome, Edge) play audio through a specific audio sub-process (e.g. utility audio service). Auralis 8D inspects active audio sessions and captures the exact process tree rendering the audio.
