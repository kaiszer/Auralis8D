@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1

cl /O2 /LD /std:c++17 /EHsc /I"src\Auralis8D.DSP\include" "src\Auralis8D.DSP\src\DSPProcessor.cpp" "src\Auralis8D.DSP\src\Auralis8D_DSP.cpp" /Fe:"src\Auralis8D.DSP\Auralis8D.DSP.dll"
if errorlevel 1 exit /b 1

echo Build successful
