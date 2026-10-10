@echo off
rem Builds and runs the host test of the UI mask's pure logic.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\ui_mask_test.exe ui_mask_test.cpp ..\GPTP\SCBW\ui_mask.cpp > out\ui_mask_build.log
if errorlevel 1 (
	type out\ui_mask_build.log
	exit /b 1
)
out\ui_mask_test.exe
