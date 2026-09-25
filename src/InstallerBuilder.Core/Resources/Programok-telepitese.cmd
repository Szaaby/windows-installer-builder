@echo off
rem Programok telepitese - kezi ujrainditas (Windows Telepito Keszito).
rem A szkript maga ker rendszergazdai jogot.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Programs.ps1"
