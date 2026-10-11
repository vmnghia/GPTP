@echo off
rem Builds and runs the host test of the build progress rules.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\build_progress_test.exe build_progress_test.cpp ..\GPTP\SCBW\build_progress.cpp > out\build_progress_build.log
if errorlevel 1 (
	type out\build_progress_build.log
	exit /b 1
)
out\build_progress_test.exe
