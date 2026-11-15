@echo off
setlocal
set "LALA_SOURCE=%~f1"
pushd "%~dp0"
set "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
if not defined PROGRAMFILES set "PROGRAMFILES=%SystemDrive%\Program Files"
if not defined PROGRAMFILES(X86) set "PROGRAMFILES(X86)=%SystemDrive%\Program Files (x86)"
if not defined PROGRAMDATA set "PROGRAMDATA=%SystemDrive%\ProgramData"
set "DOTNET_EXE=%PROGRAMFILES%\dotnet\dotnet.exe"
if not defined LALA_SOURCE set "LALA_SOURCE=%~dp0..\.."
if not defined DalamudLibPath (
    echo Set DalamudLibPath to an existing runtime directory containing Dalamud.dll, Dalamud.Bindings.ImGui.dll, and cimgui.dll.
    popd
    exit /b 2
)
for %%F in (Dalamud.dll Dalamud.Bindings.ImGui.dll cimgui.dll) do (
    if not exist "%DalamudLibPath%\%%F" (
        echo Missing required existing runtime file: %%F
        popd
        exit /b 2
    )
)
"%DOTNET_EXE%" run --project "%~dp0LazyCrucible.UiHarness.csproj" --configuration Release --property:UseSharedCompilation=false --property:LalaSourceRoot="%LALA_SOURCE%" --property:DalamudLibPath="%DalamudLibPath%"
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
