@echo off
setlocal

REM ch0000 single combined FBX export with all MOTLIST animations.

set "EXPORTER=%~dp0bin\Release\net10.0\REE-Content-Exporter.exe"
set "MESH=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch00\ch0000\00\ch0000_00_playergame.mesh.251121828"
set "MOTLIST_DIR=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\animation\ch\ch00\ch0000\motlist"
set "OUTPUT=C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0000_all_motlists.fbx"

if not exist "%EXPORTER%" (
  echo Missing exporter: "%EXPORTER%"
  echo Build first with: dotnet build -c Release
  set "EXIT_CODE=1"
  goto :error
)

"%EXPORTER%" --mesh "%MESH%" --motlist-dir "%MOTLIST_DIR%" --no-placeholder-animation-bones --texture-format png --fbx-scale 100 --output "%OUTPUT%"

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :error

endlocal
exit /b 0

:error
echo.
echo Export failed with exit code %EXIT_CODE%.
echo Press any key to close this window.
pause >nul
endlocal & exit /b %EXIT_CODE%


