@echo off
setlocal

REM ch0100 split-by-motlist export.
REM Includes mesh folders 00, 10, 20, and 40.
REM Intentionally excludes folders 15 and 45.

set "EXPORTER=%~dp0bin\Release\net10.0\REE-Content-Exporter.exe"
set "MESH00=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828"
set "MESH10=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828"
set "MESH20=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828"
set "MESH40=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828"
set "MOTLIST_DIR=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\animation\ch\ch01\ch0100\motlist"
set "OUTPUT=C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_split_motlists_except_15_45.glb"

if not exist "%EXPORTER%" (
  echo Missing exporter: "%EXPORTER%"
  echo Build first with: dotnet build -c Release
  set "EXIT_CODE=1"
  goto :error
)

"%EXPORTER%" --mesh "%MESH00%" --additional-mesh "%MESH10%" --additional-mesh "%MESH20%" --additional-mesh "%MESH40%" --motlist-dir "%MOTLIST_DIR%" --split-motlists --no-placeholder-animation-bones --texture-format png --output "%OUTPUT%"

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



