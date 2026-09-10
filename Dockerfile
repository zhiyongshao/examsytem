# ============================================================
#  ExamSystem —— 多阶段 Docker 构建
# ------------------------------------------------------------
#  阶段1 build : 用 SDK 镜像编译并发布
#  阶段2 runtime: 用精简 aspnet 镜像运行，自带 libgdiplus 兜底
#
#  注意：本项目是 net8.0（已跨平台），无需 Windows 运行时。
#  不要在 Linux 上跑 Windows 专用的 build_helper.py / start.bat。
# ============================================================

# ---------- 阶段 1：构建 ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 只拷 csproj 以利用层缓存（依赖不变则跳过 restore）
COPY ExamSystem.csproj .
RUN dotnet restore ExamSystem.csproj

# 拷入全部源码并发布（bin/obj/db 已被 .dockerignore 排除）
COPY . .
RUN dotnet publish ExamSystem.csproj -c Release -o /app/publish

# ---------- 阶段 2：运行 ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# ClosedXML 在 Linux 导出 Excel 的保险（字体度量相关）。
# ClosedXML 0.102+ 已不再硬依赖 System.Drawing.Common，多数场景不需要；
# 若线上导出 Excel 偶发异常，保留下面这行即可，不需要可删除。
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgdiplus libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

# 持久化数据目录（SQLite 库文件），确保运行用户可写
RUN mkdir -p /data && chown -R app /data
USER app

COPY --from=build /app/publish .

# Program.cs 已用 UseUrls("http://0.0.0.0:5000") 监听 5000
# EXAM_DB 指向持久卷，避免容器重启丢库
ENV EXAM_DB="Data Source=/data/exam.db" \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000
ENTRYPOINT ["dotnet", "ExamSystem.dll"]
