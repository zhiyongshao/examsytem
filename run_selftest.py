#!/usr/bin/env python3
# Windows 原生一键端到端自测（无需 bash）：
#   restore(带重试) -> build --no-restore -> 起隔离库服务 -> 等就绪 -> 跑 selftest.py -> 关停清理
# 由 run_selftest.bat 调用；也可直接用 `py run_selftest.py` / `python run_selftest.py` 运行。
#
# 说明：本机 .NET SDK 8.0.424 在 F: 盘项目上执行 restore/build 时偶发抛
#   NuGet.targets(745,5): Value cannot be null. (Parameter 'path1')
# 根因是跨盘 TEMP/资产文件路径计算；统一交给 build_helper 处理
#   （钉同盘 TEMP -> restore 重试 -> patch assets -> build --no-restore）。
import os
import sys
import time
import subprocess
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
IS_WIN = os.name == "nt"

# dotnet：优先系统安装路径，否则回退 PATH 上的 dotnet
DOTNET = r"C:\Program Files\dotnet\dotnet.exe"
if not os.path.exists(DOTNET):
    DOTNET = "dotnet"

sys.path.insert(0, HERE)
from build_helper import ensure_build  # noqa: E402

DB = os.path.join(HERE, "selftest_run.db")


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


def main():
    cleanup_db()

    print("[1/4] Restore + build (Release) ...")
    rc = ensure_build("Release")
    if rc != 0:
        print("!! build failed")
        return rc

    print("[2/4] Starting server with isolated DB...")
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

    print("[3/4] Running selftest.py...")
    rc = subprocess.run([sys.executable, os.path.join(HERE, "selftest.py")], cwd=HERE).returncode

    print(f"[4/4] selftest exit code: {rc}")
    stop_server(srv)
    cleanup_db()
    return rc


if __name__ == "__main__":
    sys.exit(main())
