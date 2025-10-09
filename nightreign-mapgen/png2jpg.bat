@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM === Settings ===
set "BASE=%~dp0"
set "SRC=%BASE%output"
set "DST=%BASE%output-jpg"
set "Q=90"  REM JPEG quality 0–100

REM === Check ImageMagick ===
where magick >NUL 2>&1
if errorlevel 1 (
  echo [ERROR] ImageMagick not found. Install it and try again.
  exit /b 1
)

REM === Ensure destination folder exists ===
if not exist "%DST%" mkdir "%DST%"

REM === Any PNGs to process? ===
dir /b "%SRC%\*.png" >NUL 2>&1
if errorlevel 1 (
  echo [INFO] No PNG files found in "%SRC%".
  exit /b 0
)

REM === Convert PNG -> JPG into output-jpg\ ===
set /a ok=0, fail=0
for %%F in ("%SRC%\*.png") do (
  set "in=%%~fF"
  set "name=%%~nF"
  magick "!in!" -quality %Q% -background white -alpha remove -alpha off "%DST%\!name!.jpg"
  if errorlevel 1 (
    echo [FAIL] %%~nxF
    set /a fail+=1
  ) else (
    echo [OK]  %%~nxF ^> !name!.jpg
    set /a ok+=1
  )
)

echo.
echo Done. Converted !ok! file^(s^) with !fail! failure^(s^).
exit /b %fail%
