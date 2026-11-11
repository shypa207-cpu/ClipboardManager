@echo off
REM Збірка Clipboard Manager. Потрібен .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
dotnet --version >nul 2>&1 || (echo Встановіть .NET 8 SDK & pause & exit /b 1)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 (echo Помилка збірки & pause & exit /b 1)
echo Готово: publish\ClipboardManager.exe
pause
