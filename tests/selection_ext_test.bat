@echo off
rem Builds and runs the host test of the extended selection's pure logic.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\selection_ext_test.exe selection_ext_test.cpp ..\GPTP\SCBW\selection_ext_core.cpp ..\GPTP\hooks\selection_ext\sel_selftest.cpp > out\build.log
if errorlevel 1 (
	type out\build.log
	exit /b 1
)
out\selection_ext_test.exe
