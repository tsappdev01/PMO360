@echo off
rem  Applies the PMO360 database scripts, in order, with sqlcmd.
rem
rem  Runs every .sql file in this folder in filename order - which is why they are numbered.
rem  A new script is picked up by being dropped in with the right number; there is no list in
rem  here to keep in step.
rem
rem  Held back unless asked for:
rem    030_permissions.sql   needs the environment's principal names set inside it first
rem    9xx_*.sql             migration loads, which are run deliberately and once
rem
rem  Usage:
rem    apply.cmd UATWEB01 PMO360 bpuser bpuser
rem    apply.cmd UATWEB01 PMO360                 (Windows authentication)
rem
rem  Stops on the first error and exits non-zero, so a failure is never buried under the
rem  scripts that follow it.

setlocal enabledelayedexpansion

set "SERVER=%~1"
set "DATABASE=%~2"
set "DBUSER=%~3"
set "DBPASS=%~4"

if "%SERVER%"=="" set "SERVER=UATWEB01"
if "%DATABASE%"=="" set "DATABASE=PMO360"

if "%DBUSER%"=="" (
    set "AUTH=-E"
    set "AUTHDESC=Windows authentication"
) else (
    set "AUTH=-U %DBUSER% -P %DBPASS%"
    set "AUTHDESC=SQL login %DBUSER%"
)

rem -b stops on error and sets ERRORLEVEL. -I turns quoted identifiers on, as the scripts expect.
rem -C trusts the server certificate, which an on-premises server with a self-signed one needs.
set "COMMON=-S %SERVER% %AUTH% -C -b -I"

echo.
echo PMO360 database scripts
echo   server   %SERVER%
echo   database %DATABASE%
echo   auth     %AUTHDESC%
echo.

set "COUNT=0"

for /f "delims=" %%f in ('dir /b /on *.sql ^| findstr /v /b /c:"030_" /c:"9"') do (
    <nul set /p "=  %%f "
    sqlcmd %COMMON% -d %DATABASE% -i "%%f" 1>nul
    if errorlevel 1 (
        echo FAILED
        echo.
        echo Re-run it on its own to see the error:
        echo   sqlcmd %COMMON% -d %DATABASE% -i "%%f"
        echo.
        echo %%f failed. Nothing after it was run.
        exit /b 1
    )
    echo ok
    set /a COUNT+=1
)

echo.
echo !COUNT! script^(s^) applied to %DATABASE% on %SERVER%.
echo.
echo 030_permissions.sql was not run. On an on-premises server with a SQL login, grant rights
echo directly instead - see db/README.md.
exit /b 0
