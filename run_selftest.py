#!/usr/bin/env python3
# Windows 原生一键端到端自测（无需 bash）：
#   restore(带重试) -> build --no-restore -> 起隔离库服务 -> 等就绪 -> 跑 selftest.py -> 关停清理
# 由 run_selftest.bat 调用；也可直接用 `py run_selftest.py` / `python run_selftest.py` 运行。
#
# 说明：本机 .NET SDK 8.0.424 在"build 顺带触发 NuGet restore"时会偶发抛
#   NuGet.targets(745,5): Value cannot be null. (Parameter 'path1')
# 因此这里把 restore 拆出来单独做，并加重试；一旦 assets.json 生成成功，后续 build/run
# 一律 --no-restore，避免再次触发该 bug。
import os
import sys
import time
import shutil
import subprocess
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
IS_WIN = os.name == "nt"

# dotnet：优先系统安装路径，否则回退 PATH 上的 dotnet
DOTNET = r"C:\Program Files\dotnet\dotnet.exe"
if not os.path.exists(DOTNET):
    DOTNET = "dotnet"

DB = os.path.join(HERE, "selftest_run.db")
ASSETS = os.path.join(HERE, "obj", "project.assets.json")

# 根因修复：.NET SDK 8.0.424 在"还原"阶段若临时目录(TEMP)与项目不在同一盘符会偶发抛
#   NuGet.targets(745,5): Value cannot be null. (Parameter 'path1')
# 项目在 F: 盘，系统 TEMP 通常在 C: 盘 → 跨盘触发。这里把 TEMP/TMP 钉到同盘目录，彻底规避。
_SAME_DRIVE_TMP = os.path.join(HERE, "..", ".nuget", "tmp")
try:
    os.makedirs(_SAME_DRIVE_TMP, exist_ok=True)
    os.environ["TEMP"] = os.environ["TMP"] = os.path.abspath(_SAME_DRIVE_TMP)
except Exception:
    pass


def cleanup_db():
    for ext in ("", "-wal", "-shm"):
        try:
            os.remove(DB + ext)
        except FileNotFoundError:
            pass


def stop_server(proc):
    if proc is None:
        return
    try:
        if IS_WIN:
            subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        else:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except Exception:
                proc.kill()
    except Exception:
        pass


def restore_with_retry():
    print("[1/5] Restoring packages (retry on flaky SDK restore bug)...")
    for attempt in range(1, 6):
        rc = subprocess.run([DOTNET, "restore", "--disable-parallel"], cwd=HERE).returncode
        if rc == 0 and os.path.exists(ASSETS):
            print("  restore OK")
            return True
        print(f"  restore attempt {attempt} failed; cleaning obj and retrying...")
        shutil.rmtree(os.path.join(HERE, "obj"), ignore_errors=True)
    return False


def main():
    cleanup_db()

    if not restore_with_retry():
        print("!! restore failed after retries")
        return 2

    print("[2/5] Building ExamSystem (Release, --no-restore)...")
    r = subprocess.run([DOTNET, "build", "-c", "Release", "--no-restore"], cwd=HERE)
    if r.returncode != 0:
        print("!! build failed")
        return r.returncode

    print("[3/5] Starting server with isolated DB...")
    env = dict(os.environ)
    env["EXAM_DB"] = "Data Source=" + DB
    log_path = os.path.join(HERE, "server.log")
    log = open(log_path, "w", encoding="utf-8", errors="replace")
    srv = subprocess.Popen(
        [DOTNET, "run", "-c", "Release", "--no-build", "--no-restore"],
        cwd=HERE, env=env, stdout=log, stderr=log,
    )

    ready = False
    for i in range(60):
        time.sleep(1)
        try:
            with urllib.request.urlopen(
                "http://localhost:5000/api/auth/candidate-hint", timeout=2
            ) as resp:
                if resp.status == 200:
                    print(f"  server ready after {i + 1}s")
                    ready = True
                    break
        except Exception:
            pass
    if not ready:
        print("!! server failed to start. tail of log:")
        log.close()
        with open(log_path, encoding="utf-8", errors="replace") as f:
            print("".join(f.readlines()[-25:]))
        stop_server(srv)
        cleanup_db()
        return 2

    print("[4/5] Running selftest.py...")
    rc = subprocess.run([sys.executable, os.path.join(HERE, "selftest.py")], cwd=HERE).returncode

    print(f"[5/5] selftest exit code: {rc}")
    stop_server(srv)
    cleanup_db()
    return rc


if __name__ == "__main__":
    sys.exit(main())
