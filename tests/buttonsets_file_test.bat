@echo off
rem Builds and runs the host test of the button set file's parser.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\buttonsets_file_test.exe buttonsets_file_test.cpp ..\GPTP\SCBW\buttonsets_file.cpp > out\buttonsets_build.log
if errorlevel 1 (
	type out\buttonsets_build.log
	exit /b 1
)
out\buttonsets_file_test.exe
