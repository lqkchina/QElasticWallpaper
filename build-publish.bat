@echo off
REM ============================================
REM  Q弹桌面壁纸 - 一键发布单文件 EXE
REM  需要已安装 .NET 7 SDK
REM ============================================
setlocal
cd /d "%~dp0src\QElasticWallpaper"

echo [1/2] 构建 Release ...
dotnet build -c Release || goto :err

echo [2/2] 发布单文件 EXE（自包含，无需装 .NET）...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish || goto :err

echo.
echo 完成！EXE 在：src\QElasticWallpaper\publish\QElasticWallpaper.exe
pause
exit /b 0

:err
echo.
echo 构建失败，请确认已安装 .NET 7 SDK：dotnet --version
pause
exit /b 1
