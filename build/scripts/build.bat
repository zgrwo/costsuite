@echo off
REM BOM Suite Build Script (Batch wrapper)
REM Usage: build.bat [Release|Debug]

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration %CONFIG%
if not defined CI pause
