#!/usr/bin/env bash
# 一键端到端自测：build -> 起隔离库服务 -> 等就绪 -> 跑 selftest.py -> 关停清理
set -u
EXAM_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$EXAM_DIR"

DOTNET="/c/Program Files/dotnet/dotnet.exe"
PY="/c/Users/zhiyong.shao/.workbuddy/binaries/python/versions/3.13.12/python.exe"
DB="$EXAM_DIR/selftest_run.db"

SRV_PID=""
cleanup() {
  if [ -n "$SRV_PID" ] && kill -0 "$SRV_PID" 2>/dev/null; then
    kill "$SRV_PID" 2>/dev/null
    wait "$SRV_PID" 2>/dev/null
  fi
  rm -f "$DB" "$DB-wal" "$DB-shm"
}
trap cleanup EXIT

echo "[1/4] Building ExamSystem (Release)..."
"$DOTNET" build -c Release --no-incremental 2>&1 | tail -4

rm -f "$DB" "$DB-wal" "$DB-shm"
LOG=/tmp/exam_selftest.log
echo "[2/4] Starting server with isolated DB..."
EXAM_DB="Data Source=$(pwd -W)/selftest_run.db" "$DOTNET" run -c Release --no-build > "$LOG" 2>&1 &
SRV_PID=$!

ready=0
for i in $(seq 1 60); do
  sleep 1
  if grep -q "Application started" "$LOG" 2>/dev/null; then echo "  server ready after ${i}s"; ready=1; break; fi
  if grep -qi "Unhandled exception\|crit:" "$LOG" 2>/dev/null; then echo "  server errored"; break; fi
done
if [ "$ready" -ne 1 ]; then
  echo "!! server failed to start. tail of log:"; tail -20 "$LOG"
  exit 2
fi
# 用 python 做一次真实连通性验证（curl 在本环境不稳定）
if ! "$PY" -c "import urllib.request,sys; urllib.request.urlopen('http://localhost:5000/api/auth/candidate-hint',timeout=5); print('  connectivity ok')" 2>/dev/null; then
  echo "!! connectivity check failed"; tail -20 "$LOG"; exit 2
fi

echo "[3/4] Running selftest.py..."
"$PY" selftest.py
RC=$?

echo "[4/4] selftest exit code: $RC"
exit $RC
