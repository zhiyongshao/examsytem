@echo off
cd /d "%~dp0"
echo ========================================
echo   ExamSystem - Online Exam Server
echo   Home:     http://localhost:5000
echo   Swagger:  http://localhost:5000/swagger
echo ========================================
"C:\Program Files\dotnet\dotnet.exe" run -c Release
pause
