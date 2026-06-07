@echo off
set GAME_EXE=%~dp0GameData\SummerInHeat.exe
if not exist "%GAME_EXE%" goto :error
cd /d "%~dp0GameData"
start "" "%GAME_EXE%" --vr
exit /b 0

:error
echo Game exe not found: %GAME_EXE%
pause
exit /b 1
