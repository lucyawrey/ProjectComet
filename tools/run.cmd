@echo off
rem Runs one of the tools in this folder (each a C# file app, tools\<tool>.cs) with its arguments:
rem
rem   tools\run <tool> [arguments]      for example tools\run unity-batch test, or tools\run unity-batch --help
rem
rem run --file works in any folder (the short dotnet <file>.cs fails in one with several project files on
rem older SDKs). The -- keeps dotnet from taking arguments it knows (--help, --no-build, -p, --) for itself. The whole command
rem line goes on as typed, the tools name first (the tool drops it), since cmd would split each argument at = ; ,
setlocal
set "tool=%~1"
if "%tool%"=="" goto usage
if not exist "%~dp0%tool%.cs" goto usage
set COMET_TOOLS_RUN_CMD=1
dotnet run --file "%~dp0%tool%.cs" -- %*
exit /b %errorlevel%
:usage
echo Usage: tools\run ^<tool^> [arguments]. The tools: 1>&2
for %%f in ("%~dp0*.cs") do echo   %%~nf 1>&2
exit /b 2
