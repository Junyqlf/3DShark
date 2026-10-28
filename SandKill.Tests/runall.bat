@echo off
chcp 65001 >nul
SandKill.Tests.exe
echo TESTS_RC=%ERRORLEVEL%
Fuzz2.exe
echo FUZZ_RC=%ERRORLEVEL%
HeroTests.exe
echo HERO_RC=%ERRORLEVEL%
Gen3Tests.exe
echo GEN3_RC=%ERRORLEVEL%
