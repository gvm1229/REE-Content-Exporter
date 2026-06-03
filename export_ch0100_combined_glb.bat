@echo off
setlocal

REM Verified ch0100 combined export.
REM Includes mesh folders 00, 10, 20, and 40.
REM Intentionally excludes folders 15 and 45.

set "EXPORTER=%~dp0bin\Release\net10.0\REE-Content-Exporter.exe"
set "MESH00=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\00\ch0100_00.mesh.251121828"
set "MESH10=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\10\ch0100_10.mesh.251121828"
set "MESH20=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\20\ch0100_20.mesh.251121828"
set "MESH40=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\ch\ch01\ch0100\40\ch0100_40_neo.mesh.251121828"
set "MOTLIST_DIR=D:\RE_EXTRACT\PRAG_EXTRACT\re_chunk_000\natives\STM\character\animation\ch\ch01\ch0100\motlist"
set "OUTPUT=C:\Users\hojin\Downloads\PRAG_PROJ\ree_exporter\ch0100_combined_except_15_45_all_animations.glb"

if not exist "%EXPORTER%" (
  echo Missing exporter: "%EXPORTER%"
  echo Build first with: dotnet build -c Release
  exit /b 1
)

"%EXPORTER%" ^
  --mesh "%MESH00%" ^
  --additional-mesh "%MESH10%" ^
  --additional-mesh "%MESH20%" ^
  --additional-mesh "%MESH40%" ^
  --motlist-dir "%MOTLIST_DIR%" ^
  --texture-format png ^
  --output "%OUTPUT%"

endlocal

