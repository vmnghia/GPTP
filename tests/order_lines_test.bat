@echo off
rem Builds and runs the host test of the order lines' pure logic.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\order_lines_test.exe order_lines_test.cpp ..\GPTP\SCBW\order_lines.cpp > out\order_lines_build.log
if errorlevel 1 (
	type out\order_lines_build.log
	exit /b 1
)
out\order_lines_test.exe
