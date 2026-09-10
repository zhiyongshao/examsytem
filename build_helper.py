#!/usr/bin/env python3
# ============================================================
#  统一构建入口：ensure env -> restore(重试) -> patch assets -> build --no-restore
# ------------------------------------------------------------
#  把「构建环境修复」固化进构建流程，任何环境（run_selftest / start.bat /
#  run_selftest.sh）都走同一套逻辑。
#
#  真实根因（2026-09-10 定位）：
#   本机 .NET SDK 8.0.424 本身完好。`dotnet restore/build` 失败是因为启动
#   dotnet 的 shell 缺少标准 Windows 系统环境变量（SystemRoot / windir /
#   ProgramData / ALLUSERSPROFILE / ProgramFiles / APPDATA 等）。缺失时，
#   .NET 进程内 Environment.GetFolderPath(CommonApplicationData) 取到 null，
#   NuGet 在构造机器级配置目录 XPlatMachineWideSetting 时
#   Path.Combine(null, …) 抛 "Value cannot be null (Parameter 'path1')"。
#   表现为 restore 在 NuGet.targets(745,5) 崩溃，build 在加载锁文件时崩溃，
#   甚至 `dotnet --info` 的 workload 枚举也崩（InstallerBase..cctor NRE）。
#   只要补足这些变量即可正常 restore/build，无需重装 SDK。
#
#  本脚本作为【防御性兜底】：
#   - _ensure_windows_env()：缺失时补齐全套 Windows 系统变量（不动已有值）；
#   - restore 失败时【不删除】obj（避免误删可用的 assets.json）；
#   - 每次 restore 后跑一次 _patch_assets（幂等，专治个别 SDK 缺失 path 字段）；
#   - 最终 build --no-restore。
# ============================================================
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DOTNET = r"C:\Program Files\dotnet\dotnet.exe"
if not os.path.exists(DOTNET):
    DOTNET = "dotnet"
ASSETS = os.path.join(HERE, "obj", "project.assets.json")

# 复用同目录的 _patch_assets.py（确保 import 失败也能直接 exec）
sys.path.insert(0, HERE)
try:
    from _patch_assets import patch_assets
except Exception:
    def patch_assets(path=None):
        return False


def _ensure_windows_env():
    """补齐全套 Windows 系统环境变量（缺失时才设，不动已有值）。

    根因：某些 shell（如本 agent 的 Git-Bash 沙箱）启动 dotnet 时不带
    SystemRoot/windir/ProgramData/ALLUSERSPROFILE/ProgramFiles/APPDATA 等变量，
    导致 .NET 进程内 GetFolderPath(CommonApplicationData) 取到 null，NuGet 在
    构造机器级配置目录时 Path.Combine(null,…) 抛 path1。SDK 本身完好，补变量即可。
    """
    profile = os.environ.get("USERPROFILE") or (
        os.environ.get("HOMEDRIVE", "C:") + os.environ.get("HOMEPATH", r"\Users")
    )
    defaults = {
        "SystemRoot": r"C:\Windows",
        "windir": r"C:\Windows",
        "ProgramData": r"C:\ProgramData",
        "ALLUSERSPROFILE": r"C:\ProgramData",
        "ProgramFiles": r"C:\Program Files",
        "CommonProgramFiles": r"C:\Program Files\Common Files",
        "APPDATA": os.path.join(profile, "AppData", "Roaming"),
        "LOCALAPPDATA": os.path.join(profile, "AppData", "Local"),
    }
    for k, v in defaults.items():
        os.environ.setdefault(k, v)


def _pin_temp():
    """把 TEMP/TMP 钉到同盘目录，规避跨盘导致的 NuGet path1 报错。"""
    tmp = os.path.abspath(os.path.join(HERE, "..", ".nuget", "tmp"))
    try:
        os.makedirs(tmp, exist_ok=True)
        os.environ["TEMP"] = os.environ["TMP"] = tmp
        os.environ["TMPDIR"] = tmp
    except Exception:
        pass


def _restore_with_retry(attempts=5):
    _ensure_windows_env()
    for i in range(1, attempts + 1):
        rc = subprocess.run([DOTNET, "restore", "--disable-parallel"], cwd=HERE).returncode
        patch_assets()  # 幂等修补（即便 restore 报 path1，也可能残留部分 assets）
        if rc == 0 and os.path.exists(ASSETS):
            print("[build] restore OK")
            return True
        print(f"[build] restore 尝试 {i}/{attempts} 失败，重试…")
        time.sleep(1)
    # 即便最后一次 rc!=0，若已有可用的 assets.json 仍可继续 build
    return os.path.exists(ASSETS)


def ensure_build(config="Release"):
    _ensure_windows_env()
    _pin_temp()
    _restore_with_retry()
    patch_assets()
    r = subprocess.run([DOTNET, "build", "-c", config, "--no-restore"], cwd=HERE)
    return r.returncode


if __name__ == "__main__":
    sys.exit(ensure_build())
