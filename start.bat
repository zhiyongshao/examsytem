@echo off
chcp 65001 >nul 2>&1
title ExamSystem - 在线考试系统
echo ========================================
echo   在线考试系统 - 服务端
echo   前端首页: http://localhost:5000
echo   API文档:  http://localhost:5000/swagger
echo ========================================
echo.
cd /d "%~dp0"
"C:\Program Files\dotnet\dotnet.exe" run -c Release
pause
