@echo off
chcp 65001 >nul
cd /d "%~dp0"
title SandKill 无头测试

rem 找 Visual Studio 2022 自带的 Roslyn 编译器（任一版本）
set "CSC="
for %%V in (Community Professional Enterprise) do (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\%%V\MSBuild\Current\Bin\Roslyn\csc.exe" (
        set "CSC=C:\Program Files\Microsoft Visual Studio\2022\%%V\MSBuild\Current\Bin\Roslyn\csc.exe"
    )
)
if "%CSC%"=="" (
    echo [错误] 没找到 csc.exe。请安装 Visual Studio 2022，或编辑本脚本手动指定编译器路径。
    pause
    exit /b 1
)

echo 编译 Core + 测试...
"%CSC%" /nologo /target:exe /out:SandKill.Tests.exe Program.cs ..\Assets\Core\*.cs
if errorlevel 1 (
    echo [错误] 编译失败，详见上方报错。
    pause
    exit /b 1
)
"%CSC%" /nologo /target:exe /main:SandKill.Fuzz2.Program /out:Fuzz2.exe Fuzz2.cs ..\Assets\Core\*.cs
if errorlevel 1 (
    echo [错误] Fuzz2 编译失败，详见上方报错。
    pause
    exit /b 1
)
"%CSC%" /nologo /target:exe /main:SandKill.HeroTests.Program /out:HeroTests.exe HeroTests.cs ..\Assets\Core\*.cs
if errorlevel 1 (
    echo [错误] HeroTests 编译失败，详见上方报错。
    pause
    exit /b 1
)

echo.
SandKill.Tests.exe
set "RC=%ERRORLEVEL%"
echo.
echo 运行 Fuzz2（回合刷新随机扫描）...
Fuzz2.exe
set "RC2=%ERRORLEVEL%"
echo.
echo 运行 HeroTests（英雄技能正确性）...
HeroTests.exe
set "RC3=%ERRORLEVEL%"
echo.
if "%RC%"=="0" if "%RC2%"=="0" if "%RC3%"=="0" (echo 全部通过！) else (echo 有失败项，请向上翻看 ✗ 行)
pause
if not "%RC%"=="0" exit /b %RC%
if not "%RC2%"=="0" exit /b %RC2%
exit /b %RC3%
