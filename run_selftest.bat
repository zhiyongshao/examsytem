@echo off
rem ============================================================
rem  ExamSystem 一键端到端自测（Windows 原生，无需 bash）
rem  用法：双击本文件，或在 cmd 里执行 run_selftest.bat
rem ============================================================
setlocal
set "EXAM_DIR=%~dp0"

rem --- 定位 Python：WorkBuddy 托管 Python -> py 启动器 -> python ---
set "PY="
if exist "C:\Users\zhiyong.shao\.workbuddy\binaries\python\versions\3.13.12\python.exe" (
    set "PY=C:\Users\zhiyong.shao\.workbuddy\binaries\python\versions\3.13.12\python.exe"
)
if not defined PY (
    py --version >nul 2>&1 && set "PY=py"
)
if not defined PY (
    python --version >nul 2>&1 && set "PY=python"
)
if not defined PY (
    echo [ERR] 找不到 Python。请安装 Python 3，或使用 WorkBuddy 自带的托管 Python。
    exit /b 2
)

"%PY%" "%EXAM_DIR%run_selftest.py"
exit /b %errorlevel%
