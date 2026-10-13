@echo off
rem Builds and runs the host test of the smart rally rules.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\smart_rally_test.exe smart_rally_test.cpp ..\GPTP\SCBW\smart_rally.cpp > out\smart_rally_build.log
if errorlevel 1 (
	type out\smart_rally_build.log
	exit /b 1
)
out\smart_rally_test.exe
