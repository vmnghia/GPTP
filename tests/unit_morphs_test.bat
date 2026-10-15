@echo off
rem Builds and runs the host test of the morph table and the smart larva pick.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\unit_morphs_test.exe unit_morphs_test.cpp ..\GPTP\SCBW\unit_morphs.cpp ..\GPTP\SCBW\smart_larva.cpp > out\unit_morphs_build.log
if errorlevel 1 (
	type out\unit_morphs_build.log
	exit /b 1
)
out\unit_morphs_test.exe
