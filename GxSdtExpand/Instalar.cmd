@echo off
rem Instala GxSdtExpand en GeneXus 18 U15. GeneXus tiene que estar cerrado.
rem Desde el código fuente, cada vez sube la versión y arma dist\GxSdtExpand-<versión>.zip para compartir.
rem Para desinstalar: Instalar.cmd -Uninstall
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1" %*
echo.
pause
