@echo off
setlocal
set "PET_DIR=%~dp0DesktopPet-1.0-Refined\dist\DesktopPet-1.0-Refined-0.1.2-win-x64-self-contained"
set "PET_EXE=%PET_DIR%\DesktopPet1Refined.exe"

if not exist "%PET_EXE%" (
    echo DesktopPet animation executable was not found:
    echo %PET_EXE%
    pause
    exit /b 1
)

start "" /D "%PET_DIR%" "%PET_EXE%"
endlocal
