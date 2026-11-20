@echo off
setlocal
pushd "%~dp0" || exit /b 1
start "" /wait "Display Settings.exe" --play
set "game_exit=%errorlevel%"
popd
exit /b %game_exit%
