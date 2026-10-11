@echo off
rem Builds and runs the host test of the line walk's pure logic.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\line_walk_test.exe line_walk_test.cpp ..\GPTP\graphics\line_walk.cpp > out\line_walk_build.log
if errorlevel 1 (
	type out\line_walk_build.log
	exit /b 1
)
out\line_walk_test.exe
