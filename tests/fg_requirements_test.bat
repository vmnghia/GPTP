@echo off
rem Builds and runs the host test of FireGraft's requirement sections.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\fg_requirements_test.exe fg_requirements_test.cpp ..\GPTP\SCBW\fg_requirements.cpp > out\fg_requirements_build.log
if errorlevel 1 (
	type out\fg_requirements_build.log
	exit /b 1
)
out\fg_requirements_test.exe
