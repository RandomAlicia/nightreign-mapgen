@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem run from the project folder
pushd nightreign-mapgen || goto :eof

rem === choose mode ===
if "%~1"=="" goto :ALL

rem -------- Mode 1: specific IDs (space/comma separated) --------
set "ARGS=%*"
set "ARGS=%ARGS:,= %"

rem count how many actually exist
set /a TOTAL=0
for %%A in (%ARGS%) do (
  call :pad3 "%%~A"
  if exist "..\data\pattern\pattern_!PAD!.json" set /a TOTAL+=1
)
if !TOTAL! EQU 0 (
  echo Nothing to render.
  popd & exit /b 0
)

set /a DONE=0
for %%A in (%ARGS%) do (
  call :pad3 "%%~A"
  if exist "..\data\pattern\pattern_!PAD!.json" (
    dotnet run -- "..\data\pattern\pattern_!PAD!.json" >nul 2>&1
    set /a DONE+=1
    echo !DONE!/!TOTAL!
  )
)
popd & exit /b 0

rem -------- Mode 2: ALL patterns in ..\data\pattern --------
:ALL
set /a TOTAL=0
for %%F in (..\data\pattern\pattern_*.json) do set /a TOTAL+=1
if !TOTAL! EQU 0 (
  echo Nothing to render.
  popd & exit /b 0
)

set /a DONE=0
for %%F in (..\data\pattern\pattern_*.json) do (
  dotnet run -- "%%F" >nul 2>&1
  set /a DONE+=1
  echo !DONE!/!TOTAL!
)
popd & exit /b 0

:pad3
set "NUM=%~1"
set "NUM=%NUM: =%"
set "NUM=%NUM:"=%"
set "PADTMP=000%NUM%"
set "PAD=!PADTMP:~-3!"
exit /b 0
