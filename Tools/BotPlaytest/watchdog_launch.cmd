@echo off
REM ASCII ONLY. cmd.exe reads .cmd in the system ANSI codepage (CP949 here),
REM so UTF-8 Korean comments corrupt the batch parser. Do not add Korean text to this file.
REM
REM Purpose: launch the bot watchdog DETACHED from the calling shell.
REM Starting it with Start-Process from inside Claude Code's PowerShell tool does not survive:
REM the watchdog died silently on 2026-09-19 and an 8-minute Unity hang went unnoticed.
REM `start` reparents the process so it outlives this session.
REM
REM The watchdog writes its own log to BotRuns\watchdog.log (Say appends there),
REM so no output redirection is needed here.
REM
REM Usage: watchdog_launch.cmd [deadline HH:MM] [max restarts]
setlocal
set DEADLINE=%~1
set RESTARTS=%~2
if "%DEADLINE%"=="" set DEADLINE=10:00
if "%RESTARTS%"=="" set RESTARTS=12
start "BotWatchdog" /min powershell.exe -ExecutionPolicy Bypass -File "%~dp0watchdog.ps1" -Deadline %DEADLINE% -MaxRestarts %RESTARTS%
endlocal
