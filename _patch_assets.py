#!/usr/bin/env python3
# ============================================================
#  assets.json 修补脚本（防御 SDK 8.0.424 restore / 资产加载缺陷）
# ------------------------------------------------------------
#  本机 .NET SDK 8.0.424 在本环境下 `dotnet restore` 会确定性失败
#  （NuGet.targets(745,5): Value cannot be null (Parameter 'path1')），
#  且生成的 obj/project.assets.json 即便结构完整也会被 `dotnet build`
#  的 ResolvePackageAssets / LockFileReader 以同样的 "path1" 报错拒绝加载
#  （连手写的空文件都报错；`dotnet --info` 本身也崩在
#  InstallerBase..cctor 的 NullReferenceException）。
#  即：本机 SDK 安装已损坏，restore 与资产加载均不可用。
#
#  本脚本作为【次级防御】，幂等修复 assets.json 中缺失的 `path` 字段
#  （1) 为每个 library 补 path；2) 为每个 target 下的 package 补 path；
#  缺失时按 "id/version" 推导）。即使文件已经正确，重复执行也无害。
#
#  注意：仅靠本脚本无法在损坏的 SDK 上完成构建。要让 build 跑通，要么
#  在健康的 SDK 上构建，要么修复本机 SDK（重装/修复 .NET 8.0.424），要么
#  依赖一份「已存在且可加载」的 assets.json 走 `dotnet build --no-restore`
#  （由 build_helper.py 负责兜底）。
#
#  用法：
#    python _patch_assets.py                # 修补 ./obj/project.assets.json
#    python _patch_assets.py <path>         # 修补指定路径
# ============================================================
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def patch_assets(path=None):
    if path is None:
        path = os.path.join(HERE, "obj", "project.assets.json")
    if not os.path.exists(path):
        print(f"[patch_assets] 未找到 assets.json：{path}（跳过）")
        return False

    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    libs = data.get("libraries", {})
    # 1) 让每个 library 条目都有 path（按 key 推导兜底）
    for name, lib in libs.items():
        if "/" not in name:
            continue
        if not lib.get("path"):
            lib["path"] = name.lower()

    # 2) 让每个 target 下的 package 条目都有 path
    targets = data.get("targets", {})
    fixed = 0
    for tfrags in targets.values():
        for name, lib in tfrags.items():
            if "/" not in name:
                continue
            if not lib.get("path"):
                lib["path"] = libs.get(name, {}).get("path") or name.lower()
                fixed += 1

    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))

    print(f"[patch_assets] 已修补 {path}（补 path 字段 {fixed} 处）")
    return True


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else None
    patch_assets(target)
