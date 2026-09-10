@echo off
chcp 65001 >nul
rem ============================================================
rem  ExamSystem 服务启动（Windows 原生）
rem  统一走 build_helper.py：钉同盘 TEMP -> restore 重试 -> patch assets -> build
rem  只在 assets.json 已可用时（绝大多数情况）即可编译通过，规避 SDK 8.0.424 偶发缺陷。
rem ============================================================
cd /d "%~dp0"
setlocal

set "PY=C:\Users\zhiyong.shao\.workbuddy\binaries\python\versions\3.13.12\python.exe"
if not exist "%PY%" set "PY=py"
if not exist "%PY%" set "PY=python"

echo ========================================
echo   ExamSystem - Online Exam Server
echo   Home:     http://localhost:5000
echo   Swagger:  http://localhost:5000/swagger
echo ========================================

echo [1/2] 构建 (restore + patch assets + build --no-restore)...
"%PY%" build_helper.py
if errorlevel 1 (
  echo [!] 构建失败，请检查上方日志。
  pause
  exit /b 1
)

echo [2/2] 启动服务...
"C:\Program Files\dotnet\dotnet.exe" run -c Release --no-build --no-restore
pause
