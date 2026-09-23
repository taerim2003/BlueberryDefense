@echo off
rem QA runner - starts detached in a minimized window. Args are passed through to qa-runner.js.
rem   Tools\QA\qa-runner.cmd --deadline 07:00 --instances 2 --chaos 1
rem Stop: create QARuns\runner.stop
cd /d "%~dp0\..\.."
start "qa-runner" /min node Tools\QA\qa-runner.js %*
