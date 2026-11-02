@echo off
rem Runs one of the tools in this folder (each a C# file app, tools\<tool>.cs) with its arguments:
rem
rem   tools\run <tool> [arguments]      for example tools\run unity-batch test, or tools\run unity-batch --help
rem
rem The -- keeps dotnet from taking arguments it knows (--help, --no-build, -p, --) for itself.
setlocal
rem The tools folder, kept before shift: shift moves %0 too.
set "here=%~dp0"
set "tool=%~1"
if "%tool%"=="" goto usage
if not exist "%here%%tool%.cs" goto usage
rem shift does not change %*, so the arguments after the tool are collected one by one.
set args=
:collect
shift
if "%~1"=="" goto run
set args=%args% %1
goto collect
:run
dotnet "%here%%tool%.cs" -- %args%
exit /b %errorlevel%
:usage
echo Usage: tools\run ^<tool^> [arguments]. The tools: 1>&2
for %%f in ("%here%*.cs") do echo   %%~nf 1>&2
exit /b 2
