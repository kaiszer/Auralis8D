# Session Summary - Auralis 8D

## Estado Inicial del Proyecto
- Workspace inicial vacio (`c:\Users\Kaisz\Documents\codes\8dAudio`).
- Entorno de desarrollo identificado:
  - Sistema Operativo: Windows 11 Build 26200 (soporta nativamente `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` y `ActivateAudioInterfaceAsync`).
  - .NET SDK: .NET SDK 8.0.417 instalado en el sistema.
  - Compilador C++: MSVC 14.41.34120 (Visual Studio 2022 Community) con Windows SDK 10.0.22621.0.
  - Repositorio de referencia (`https://github.com/devinayeon/8D-Audio-Converter`): Inspeccionado. Se analizo el algoritmo matematico de panning estéreo de potencia constante basado en oscilador sinusoidal (equal-power law: $\cos(\theta)$ para canal izquierdo y $\sin(\theta)$ para canal derecho) y ensanchamiento estéreo.

## Acciones Realizadas en Esta Sesion
1. **Inspeccion de Herramientas y SDKs**:
   - Comprobacion de presencia de `audioclientactivationparams.h` en Windows SDK 10.0.22621.0. Se confirmaron las estructuras `AUDIOCLIENT_ACTIVATION_PARAMS`, `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` y el endpoint virtual `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`.
   - Clonacion y analisis temporal de las tecnicas de procesamiento de senales de `8D-Audio-Converter`.
2. **Definicion de Arquitectura y Plan**:
   - Diseno de la arquitectura modular (`Auralis8D.DSP`, `Auralis8D.Audio`, `Auralis8D.Core`, `Auralis8D.UI`, `Auralis8D.Tests`).
3. **Fase 1 - Factibilidad Tecnica y Motor DSP (C++)**:
   - Implementacion de `DSPProcessor.h` y `DSPProcessor.cpp`:
     - Acumulador de fase continuo $\theta \in [0, 2\pi)$ sin saltos de fase.
     - Ley de paneo de potencia constante (equal-power) evitando caidas de volumen en el centro.
     - Procesamiento Mid/Side para ensanchamiento de campo estéreo sin artefactos de peine.
     - Limitador de saturacion suave polinomial/tanh para evitar recortes digitales (clipping).
     - Modo bypass con transicion suave por interpolacion exponencial (sin clics ni ruidos de corte).
   - Exportacion C ABI en `Auralis8D_DSP.h` y `Auralis8D_DSP.cpp`.
   - Compilacion exitosa de `Auralis8D.DSP.dll` (x64) mediante MSVC.
   - Creacion y ejecucion de la suite de pruebas unitarias `test_dsp.exe` (todas aprobadas).
   - Prueba de concepto nativa `test_loopback_poc.exe` validando `ActivateAudioInterfaceAsync` y enumeracion de sesiones en C++.
4. **Fase 2 - Capas Core y Audio en C# / WASAPI**:
   - `Auralis8D.Core`: Modelos, catálogo de perfiles y `ConfigurationService` (persistencia JSON en `%LOCALAPPDATA%\Auralis8D\settings.json`).
   - `Auralis8D.Audio`: Wrapper P/Invoke `NativeDSP.cs`, capturador selectivo `ProcessLoopbackCapture` con silenciamiento de sesión original, renderizador `WasapiAudioRenderer` con búfer circular lock-free y `AudioEngineCoordinator`.
5. **Fase 3 - Interfaz de Usuario WPF**:
   - `Auralis8D.UI`: Estilizado moderno azul marino/cian/plata, `StereoVisualizer` vectorial de 60fps, diálogos modales para gestión de aplicaciones y configuración.
6. **Fase 4 - Suite de Pruebas Automatizadas y Empaquetado**:
   - `Auralis8D.Tests`: 10 pruebas unitarias e integrales ejecutadas con éxito.
   - Empaquetado y distribución en `dist/`.
   - Repositorio git inicializado.
7. **Resolución de Incidencia: Lista de Aplicaciones Vacía en Manage Apps**:
   - Corrección de GUIDs COM en `WasapiInterfaces.cs` contrastados contra el Windows SDK:
     - `IAudioSessionEnumerator`: Corregido a `E2F5BB11-0570-40CA-ACDD-3AA01277DEE8`.
     - `IAudioSessionControl2`: Corregido a `bfb7ff88-7239-4fc9-8fa2-07c950be9c6d` y reordenada la vtable COM completa.
     - `IActivateAudioInterfaceAsyncOperation`: Corregido a `72A22D78-CDE4-431D-B8CC-843A71199B6D`.
     - `IActivateAudioInterfaceCompletionHandler`: Corregido a `41D949AB-9862-444A-80F6-C261334DA5EB`.
   - Ampliación de `AudioSessionTracker.cs`:
     - Detección de sesiones activas de audio WASAPI y enriquecimiento con procesos interactivos de la sesión de escritorio del usuario.
     - Deduplicación de subprocesos secundarios de navegadores (Edge/Chrome) para presentar una lista limpia.
     - Filtrado de procesos del sistema/servicios mediante lista de exclusión.
8. **Resolución de Incidencias: Captura Loopback E_NOTIMPL y Controles de Reproducción 8D**:
   - *Causa raíz 1 (Captura loopback inoperativa):* Al activar el cliente virtual de loopback por proceso (`VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`), la llamada `IAudioClient.GetMixFormat` devuelve siempre `0x80004001 (E_NOTIMPL)` por especificación de WASAPI en Windows, lo que provocaba una excepción y abortaba el inicio de la captura en segundo plano.
   - *Solución 1:* Se implementó `GetDeviceMixFormat()` en `ProcessLoopbackCapture.cs`, obteniendo el formato de mezcla activo directamente del endpoint de renderizado predeterminado (`eRender`), o construyendo la estructura canónica `WAVEFORMATEXTENSIBLE` (48kHz, 2 canales, 32-bit IEEE float) para inicializar el cliente loopback.
   - *Causa raíz 2 (Superposición de tarjeta vacía y botón "Select Applications" inclickeable):* El convertidor nativo de WPF `BooleanToVisibilityConverter` no soporta `ConverterParameter=Inverse`. Al seleccionarse una aplicación (`HasSelectedApps == true`), la tarjeta de estado vacío seguía visible y se renderizaba sobre la lista de aplicaciones seleccionadas.
   - *Solución 2:* Implementación del convertidor `BoolToVisibilityConverter` en `Auralis8D.UI.Converters`, permitiendo que la tarjeta vacía colapse inmediatamente cuando existen aplicaciones seleccionadas.
   - *Causa raíz 3 (Dificultad para activar / dar "Play" a la configuración 8D):* La interfaz carecía de un botón maestro de activación destacado y explícito.
   - *Solución 3:*
     - Se incorporó un botón maestro de control interactivo en el panel principal: `8D ROTATION: ACTIVE` (con distintivo cian iluminado e indicación de "Click to bypass effect") y `8D ROTATION: BYPASSED` ("Click to play / activate 8D effect").
     - Se añadió un distintivo en vivo en la visualización estéreo: `LIVE 8D STREAMING` vs `WAITING FOR AUDIO` vs `BYPASS MODE`.
     - Se conectó el evento `SelectionChanged` en `AppProcessViewModel` y reconciliación no destructiva de la lista `SelectedApps`.
9. **Resolución de Incidencia: "Waiting for audio" y Visualizador Congelado con Spotify**:
   - *Causa raíz:* En `ProcessLoopbackCapture.cs`, el método `TryMuteOriginalSession` ejecutaba `_sessionVolume.SetMute(true)`. En la arquitectura interna de Windows Core Audio, `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` captura el flujo de la sesión *después* de la etapa de volumen y silenciamiento de sesión. Por ende, silenciar la sesión original forzaba al mezclador de Windows a generar silencio digital (`AUDCLNT_BUFFERFLAGS_SILENT`) con amplitud cero (`Max Peak: 0`). Esto impedía que el motor detectara flujo de audio (`peak > 0.001f`), dejando la aplicación en estado "Waiting for audio" con la esfera del visualizador congelada.
   - *Solución:* Se eliminó el silenciamiento forzado de la sesión en `ProcessLoopbackCapture.cs`.
   - *Validación:* En pruebas automatizadas con Spotify activo (`Spotify.exe`), la amplitud capturada subió inmediatamente a `Max Peak: 0.2016` con un flujo constante de 27,840 cuadros en tiempo real, activando el indicador `LIVE 8D STREAMING` y la oscilación fluida de la esfera en el visualizador.

## Errores Encontrados y Soluciones
- *Error:* Intento de lectura de URL de archivo individual no encontrado (`404` en `master` branch del repositorio de referencia).
  - *Solucion:* Se clono temporalmente la rama `main` y se extrajeron las ecuaciones matemáticas exactas.
- *Error:* Fallo de comando en invocacion cmd con comillas anidadas en PowerShell.
  - *Solucion:* Se creo `build_dsp.bat` nativo con `vcvars64.bat`.
- *Error de compilación CS0117:* `AUDCLNT_BUFFERFLAGS` no contenía `AUDCLNT_STREAMFLAGS_NONE`.
  - *Solucion:* Se definió `AUDCLNT_BUFFERFLAGS_NONE = 0x0` en `WasapiInterfaces.cs`.
- *Error de XAML MC3072:* La propiedad `LetterSpacing` no existe en WPF `TextBlock`.
  - *Solucion:* Se removió dicho atributo de `MainWindow.xaml`.
- *Error 0x80004002 (E_NOINTERFACE) en IAudioSessionManager2.GetSessionEnumerator:* Al dar clic en "Manage Apps" no aparecían aplicaciones.
  - *Solucion:* El GUID declarado en C# era incorrecto. Se actualizó al GUID canónico del Windows SDK (`E2F5BB11-0570-40CA-ACDD-3AA01277DEE8`) y se comprobó que devuelve correctamente las sesiones activas.
- *Error System.NotImplementedException (0x80004001 E_NOTIMPL) en IAudioClient.GetMixFormat:* Al iniciar la captura loopback sobre un proceso, la invocación fallaba.
  - *Solucion:* El cliente virtual de loopback de Windows no implementa `GetMixFormat`. Se obtiene el formato desde el dispositivo de renderizado predeterminado o se provee un `WAVEFORMATEXTENSIBLE` de punto flotante de 32 bits a 48 kHz.
- *Error de visualización en lista de aplicaciones seleccionadas:* El botón "Select Applications" permanecía superpuesto tras seleccionar una app y no permitía interacción limpia.
  - *Solucion:* Se reemplazó el convertidor nativo por `BoolToVisibilityConverter` que respeta `ConverterParameter=Inverse`, colapsando de inmediato el contenedor vacío.
- *Error de captura en silencio (Max Peak: 0 / Waiting for audio) al seleccionar una aplicación activa:* Al marcar Spotify, el visualizador no se movía y marcaba espera de audio.
  - *Solucion:* Se eliminó la llamada a `SetMute(true)` en la sesión original de WASAPI, ya que en Windows la captura loopback es post-mute y entregaba búferes de ceros. Al retirarlo, la captura transmite audio real a 48kHz (`Max Peak > 0.20`), activando el visualizador y el procesamiento 8D.
