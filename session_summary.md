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
10. **Resolución de Incidencia: Árbol Multiproceso de Spotify y Estado "Waiting for audio" al Seleccionar la Canción**:
    - *Causa raíz:* En aplicaciones con arquitectura Chromium / CEF (como Spotify, Google Chrome y Microsoft Edge), existen múltiples procesos paralelos: un proceso raíz principal (PID 17184), procesos renderizadores de pestañas o interfaz de usuario (PID 15108) con el título de la ventana ("Maribou State - Midas"), y procesos dedicados al servicio de audio (PID 15924).
      En la enumeración anterior, `AudioSessionTracker` listaba el proceso de renderizado de la ventana como un elemento separado porque tenía un `MainWindowTitle` no vacío. Al hacer clic en "Maribou State - Midas", la interfaz seleccionaba el PID 15108 (el proceso de la interfaz gráfica).
      Al invocar `AUDIOCLIENT_ACTIVATION_PARAMS` con `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` sobre el PID hijo (15108), Windows capturaba únicamente ese proceso y sus descendientes directos (ninguno), mientras que el audio era renderizado por el proceso de servicio de audio (hijo del proceso raíz 17184). Por tanto, la captura loopback recibía 28,320 cuadros por segundo pero con amplitud cero (`maxPeak = 0.0000`, silencio digital), dejando la interfaz en "Waiting for audio" y congelando el visualizador.
    - *Solución:*
      1. Se creó `ProcessTreeHelper.cs` en `Auralis8D.Audio.Interop`: utiliza la API Win32 `CreateToolhelp32Snapshot` para mapear jerarquías completas de procesos y resolver cualquier PID secundario o de interfaz gráfica hacia el PID del proceso raíz de la aplicación.
      2. En `ProcessLoopbackCapture.cs`: se implementó la resolución automática de `effectivePid = ProcessTreeHelper.GetRootProcessId(_targetPid)`. Al adjuntarse siempre al proceso raíz del árbol, la bandera `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` abarca automáticamente todo el árbol de la aplicación, incluyendo subprocesos de servicio de audio y renderizadores.
      3. En `AudioSessionTracker.cs`: se agruparon todos los subprocesos de una misma aplicación bajo un único registro canónico con el PID raíz. Si algún subproceso de la interfaz posee el título de la canción o página (como "Maribou State - Midas"), se asocia dicho título como `DisplayName` del proceso raíz, eliminando duplicados y garantizando que la selección del usuario siempre capture el flujo de audio real.
      4. En `AudioEngineCoordinator.cs`: se optimizó la persistencia del estado `IsAudioFlowing` con una ventana de decaimiento musical de 1200 ms, actualización continua del ángulo de paneo del oscilador DSP y preservación de mensajes de error de captura para evitar que sean sobrescritos prematuramente.
    - *Validación:*
      - Al probar directamente la captura sobre todos los PIDs de Spotify (15108, 2780, 6552, 15924 y 17184), todos resolvieron al proceso raíz y registraron de inmediato amplitudes activas (`maxPeak > 0.17`, cuadros recibidos > 28,000/s).
      - La suite completa de 11 pruebas automatizadas (`dotnet run --project src\Auralis8D.Tests`) aprobó al 100%.
      - La aplicación fue recompilada y publicada exitosamente en `dist/`.
11. **Publicación del Repositorio en GitHub**:
    - Se creó el repositorio público oficial en GitHub: `https://github.com/kaiszer/Auralis8D.git` utilizando las credenciales de Git Credential Manager del usuario (`kaiszer`).
    - Se configuró el remoto `origin` y se publicó la rama `master` con seguimiento automático (`git push -u origin master`).
12. **Resolución de Incidencia: Título Congelado en Spotify y Auto-recuperación de Captura**:
    - *Causa raíz 1 (Título estático al cambiar de canción):* En `MainViewModel.cs`, el ciclo de refresco actualizaba los campos de pico y actividad (`AudioPeak`, `IsActiveAudio`), pero no propagaba los cambios en `DisplayName` hacia `AppProcessViewModel`. Adicionalmente, en `AudioSessionTracker.cs`, la condición de descarte de nombres de ventana impedía sobrescribir títulos no vacíos previos, impidiendo que el nuevo título de canción se reflejara en la UI.
    - *Causa raíz 2 (Persistencia de binario anterior bloqueado):* El proceso de la aplicación previa había quedado abierto en segundo plano (PID 14040 desde las 04:48 p.m.), impidiendo que `dotnet publish` sobrescribiera los ensamblados en `dist/`. Al interactuar con la aplicación, el usuario continuaba ejecutando la versión previa al parche del proceso raíz.
    - *Solución:*
      1. Se añadió `UpdateFromModel(AppProcessItem updated)` en `AppProcessViewModel.cs`, emitiendo notificaciones `PropertyChanged(nameof(DisplayName))` para refrescar en vivo el título de la canción en la interfaz gráfica.
      2. Se incorporó escaneo nativo Win32 con `EnumWindows` en `AudioSessionTracker.cs` para detectar dinámicamente cualquier ventana con título descriptivo de canción perteneciente al clúster de procesos de Spotify.
      3. Se agregó auto-recuperación en `AudioEngineCoordinator.SyncSelectedProcesses`: si un subproceso de audio se detiene o reinicializa al cambiar de pista, el coordinador detecta `!Capture.IsRunning`, limpia el canal y reinicia la captura de forma transparente e instantánea.
       4. Se finalizó la instancia antigua y se recompilaron y publicaron los binarios actualizados en `dist/` (`Auralis8D.exe` y `Auralis8D.Audio.dll`).
13. **Resolución de Incidencia: Error COM E_NOINTERFACE (0x80004002) en IAudioClient e Implementación de Ventana de Diagnóstico (Debug)**:
    - *Causa raíz (Error COM E_NOINTERFACE 0x80004002 al activar captura en Spotify):*
      - En aplicaciones WPF, el hilo principal de la interfaz gráfica se ejecuta bajo un Single-Threaded Apartment COM (`[STAThread]`).
      - Cuando el usuario seleccionaba una aplicación (como Spotify), `MainViewModel` invocaba `ProcessLoopbackCapture.Start()` desde el hilo de interfaz (STA).
      - `ActivateAudioInterfaceAsync` delega la activación del dispositivo virtual loopback al subsistema de audio de Windows, el cual invoca `IActivateAudioInterfaceCompletionHandler.ActivateCompleted` sobre un hilo de la cola de trabajo del sistema operativo (MTA).
      - Al finalizar la espera (`handler.WaitForCompletion()`), el hilo de la interfaz STA intentaba hacer el cast explícito `_audioClient = (IAudioClient)handler.ActivatedInterface`.
      - Dado que el objeto COM subyacente residía en el apartamento MTA y las interfaces nativas de Windows Core Audio (WASAPI, como `IAudioClient` `{1CB9AD4C-DBFA-4c32-B178-C2F568A703B2}`) no implementan ni registran proxy/stub estándar en el registro de Windows, el CLR intentó realizar un marshaling COM entre apartamentos (MTA -> STA), fallando con `0x80004002 (E_NOINTERFACE: No such interface supported)`.
    - *Solución:*
      1. **Aislamiento Estricto de Apartamento COM en MTA (`ProcessLoopbackCapture.cs`):**
         - Se implementó la verificación de apartamento `Thread.CurrentThread.GetApartmentState()`. Si el hilo invocador es STA (hilo UI), la activación de `ActivateAudioInterfaceAsync`, la espera de finalización, la obtención del formato de mezcla (`GetDeviceMixFormat()`) y la inicialización de `IAudioClient` e `IAudioCaptureClient` se delegan a un hilo dedicado con `ApartmentState.MTA`.
         - Al ejecutarse la activación y el cast dentro del MTA, la llamada a `QueryInterface` ocurre dentro del mismo apartamento COM nativo sin marshaling ni proxies, resolviendo `IAudioClient` de manera inmediata y sin errores.
         - El bucle de lectura de búferes (`CaptureLoop`) y la liberación de interfaces (`Stop()` / `Dispose()`) operan de manera homogénea en hilos MTA.
      2. **Servicio Centralizado de Diagnóstico (`DebugLogger.cs` en `Auralis8D.Core.Diagnostics`):**
         - Búfer rodante de hasta 1000 entradas en memoria con marcas de tiempo de alta resolución (`HH:mm:ss.fff`), nivel de severidad (INFO, WARN, ERR), módulo de origen y excepciones completas.
         - Persistencia continua en disco en `%LOCALAPPDATA%\Auralis8D\diagnostics.log`.
         - Trazabilidad detallada en cada etapa del pipeline: detección de procesos, resolución de árboles Chromium/Spotify, llamadas WASAPI, formatos de audio y llegada del primer paquete de audio.
      3. **Ventana de Diagnóstico y Botón "DEBUG" (`DebugWindow.xaml`, `DebugViewModel.cs`, `MainWindow.xaml`):**
         - Se incorporó un botón estilizado `DEBUG` en la cabecera principal de `MainWindow.xaml` junto a "Settings".
         - La ventana `DebugWindow` presenta un visor de logs en tiempo real con resaltado cromático según severidad, botón `Copy All Logs` para copiar todo el log al portapapeles con un solo clic, acceso directo a la carpeta en el Explorador de Windows, botón de limpieza y desplazamiento automático a los registros más recientes (`Auto-scroll`).
      4. **Pruebas Automatizadas de Apartamento STA:**
         - Se incorporó la prueba unitaria `TestStaLoopbackCaptureApartmentIsolation` en `Auralis8D.Tests\Program.cs`, ejecutando la captura sobre un hilo STA explícito y validando que no ocurran excepciones ni errores `E_NOINTERFACE`.
         - Se incorporó `TestDebugLoggerFunctionality` validando almacenamiento, exportación y persistencia.
         - Las 13 pruebas automatizadas de la suite ejecutaron y aprobaron con éxito total (13/13 pasadas).
      5. **Publicación y Despliegue:**
         - Se compilaron y publicaron los binarios actualizados en `dist/` (`Auralis8D.exe`, `Auralis8D.dll`, `Auralis8D.Audio.dll`, `Auralis8D.Core.dll`, `Auralis8D.DSP.dll`).

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
- *Error de proceso hijo de interfaz gráfica sin audio en aplicaciones Chromium/Spotify:* Al seleccionar la aplicación que mostraba el nombre de la canción ("Maribou State - Midas"), la captura apuntaba al subproceso de la ventana (PID hijo) en lugar del proceso raíz, recibiendo silencio (`maxPeak=0.0000`).
  - *Solucion:* Se introdujo `ProcessTreeHelper` para agrupar procesos y resolver automáticamente cualquier PID hacia el proceso raíz del árbol (`effectivePid`), permitiendo que `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` capture todo el flujo de audio de la aplicación (`maxPeak > 0.17`) y sincronizando la rotación 8D en el visualizador.
- *Error de título de pista estático y binario bloqueado en ejecución:* Al cambiar de canción en Spotify, la interfaz no actualizaba el nombre y la aplicación antigua bloqueaba la sobreescritura de los binarios en `dist/`.
  - *Solucion:* Se implementó `UpdateFromModel` con notificación WPF en vivo, escaneo Win32 mediante `EnumWindows`, auto-recuperación de canales de captura en `AudioEngineCoordinator` y despliegue limpio de los nuevos binarios en `dist/`.
- *Error COM E_NOINTERFACE (0x80004002) al castear IAudioClient desde el hilo STA de WPF:* Al seleccionar Spotify, aparecía un mensaje de error indicando que QueryInterface falló en `{1CB9AD4C-DBFA-4c32-B178-C2F568A703B2}` con `E_NOINTERFACE`.
  - *Solucion:* El callback de `ActivateAudioInterfaceAsync` se ejecuta en un hilo MTA del sistema operativo. Intentar castear el `System.__ComObject` a `IAudioClient` desde el hilo STA de la interfaz de WPF forzaba un marshaling COM entre apartamentos que fallaba por la ausencia de proxy/stub en las interfaces WASAPI de Windows. Se encapsuló la activación y gestión de interfaces COM exclusivamente dentro de hilos MTA (`Auralis8D_MTA_Activator`), garantizando llamadas directas en el mismo apartamento sin marshaling. Adicionalmente, se creó el sistema central de diagnóstico `DebugLogger` y la ventana `DebugWindow` con botón dedicado en la interfaz para permitir monitoreo en vivo y copiado de logs con un solo clic.


