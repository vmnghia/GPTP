@echo off
rem Builds and runs the host test of the per-player slots.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\player_slots_test.exe player_slots_test.cpp > out\player_slots_build.log
if errorlevel 1 (
	type out\player_slots_build.log
	exit /b 1
)
out\player_slots_test.exe
