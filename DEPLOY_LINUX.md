# ExamSystem 部署到 Linux 服务器

本项目是 `net8.0`（已跨平台），不含任何 Windows 绑定，可直接在 Linux 上以 Docker 容器运行。

## 1. 服务器前置条件

- 一台 Linux 服务器（Ubuntu 22.04+ / Debian 12 等均可）
- 已安装 **Docker Engine** + **Docker Compose v2**
  ```bash
  # 安装 docker（Ubuntu 示例）
  sudo apt-get update && sudo apt-get install -y docker.io docker-compose-plugin
  sudo systemctl enable --now docker
  sudo usermod -aG docker $USER   # 退出重登后免 sudo
  ```

## 2. 取得代码（三选一）

### A. 从 Git 仓库拉（推荐，最干净）
```bash
git clone <你的仓库地址> /opt/ExamSystem
cd /opt/ExamSystem
```

### B. 直接从本机（Windows）把文件夹拷过去
项目文件现在在本机 `F:\workbuddy\ExamSystem`。把**整个目录**（含 `Dockerfile` / `docker-compose.yml` / `nginx/`）传到服务器即可，`bin/` `obj/` `*.db` 会被 `.dockerignore` 自动忽略，不影响构建。

- **图形化（最省事）**：用 WinSCP 连上服务器，把 `ExamSystem` 文件夹拖到 `/opt/ExamSystem`。
- **命令行（Git Bash / WSL / PowerShell+OpenSSH）**：
  ```bash
  # 在本机执行，把整个目录传到服务器的 /opt/ExamSystem
  scp -r /f/workbuddy/ExamSystem user@<服务器IP>:/opt/ExamSystem
  # 若服务器已有 /opt/ExamSystem 且想覆盖，先清掉旧目录再传
  ```
  > 注意路径：Windows 的 `F:\workbuddy\ExamSystem` 在 Git Bash 里写作 `/f/workbuddy/ExamSystem`；PowerShell 里用 `F:\workbuddy\ExamSystem`。`scp -r` 会带上 `.git` 等隐藏文件没关系，Docker 构建时会忽略。

### C. 本机先构建镜像再传（适合服务器不能联网拉 SDK 镜像）
本机需装 Docker Desktop：
```powershell
# 本机
cd F:\workbuddy\ExamSystem
docker build -t examsystem:latest .
docker save examsystem:latest -o examsystem.tar
scp examsystem.tar user@<服务器IP>:/opt/examsystem.tar
```
服务器上：`docker load -i /opt/examsystem.tar`，然后把 `docker-compose.yml` 里的 `build:` 整段删掉（只保留 `image: examsystem:latest`），再 `docker compose up -d`。

> ⚠️ **不要在 Linux 上跑 `build_helper.py` / `start.bat`** —— 它们是 Windows 专用（里面硬编码了 `C:\` 路径和 Windows 系统变量）。Docker 构建直接用 `dotnet publish`，与本机 Windows 环境无关。

## 3. 启动（最简，HTTP 暴露 5000）

```bash
# 务必先改密钥！生成强随机串替换 compose 里的 REPLACE_WITH_STRONG_RANDOM_SECRET
openssl rand -base64 48

# 编辑 docker-compose.yml，把 Jwt__Secret 改成上面的输出
nano docker-compose.yml

# 构建并后台启动
docker compose up -d --build
```

启动后访问：`http://<服务器IP>:5000`
- 后台：`/admin`  ·  考试入口短链：`/e/{slug}`
- 默认管理员账号 `admin / admin`（**上线请立即改密码**）

## 4. 数据持久化

- SQLite 库文件通过环境变量 `EXAM_DB=Data Source=/data/exam.db` 指向容器内 `/data`，
  该目录挂载到命名卷 `examdata`，**容器重建/升级不会丢数据**。
- 应用首次启动会 `EnsureCreated()` + `Seed()`，自动建好 `admin / admin` 管理员账号，
  **空库也能直接登录后台**（只是没有题目和历史考试）。
- 备份数据库：
  ```bash
  docker compose exec exam cp /data/exam.db /tmp/exam.db
  docker cp examsystem:/tmp/exam.db ./exam-$(date +%F).db
  ```
- 迁移到新服务器：把 `exam.db` 拷到新机的同名卷即可（停服务后操作）。

### 4.1 把本机现有 `exam.db` 的数据带过去（保留题目/历史）
`.dockerignore` 已排除 `*.db`，所以镜像里是空库。要保留本机数据，先正常 `up` 起一次（会建空库），再把本机 `exam.db` 覆盖进卷：

```bash
# 1) 服务器上先起服务，让卷里生成空 exam.db
docker compose up -d --build

# 2) 在本机把 exam.db 拷进容器（覆盖空库）
#    Git Bash / WSL：
scp /f/workbuddy/ExamSystem/exam.db user@<服务器IP>:/tmp/exam.db
# 服务器上执行：
docker cp /tmp/exam.db examsystem:/data/exam.db
docker compose exec exam chown app:app /data/exam.db   # 修正属主，避免 app 用户写不进
docker compose restart exam
```
> 操作前务必 `docker compose down` 停服、确认无考生正在作答，避免库文件被锁。覆盖后旧数据（题目/考试/作答）全部回来。

## 5. 加 HTTPS（生产推荐）

在主机上装 Nginx + certbot，用本项目 `nginx/exam.nginx.conf` 反代到 `127.0.0.1:5000`：

```bash
sudo apt-get install -y nginx certbot python3-certbot-nginx
sudo cp nginx/exam.nginx.conf /etc/nginx/conf.d/exam.conf
# 把文件里的 exam.example.com 改成你的真实域名，保存后：
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d exam.example.com
```

证书续期：`sudo certbot renew --dry-run`（cron 自动续）。

> 加了 Nginx 后，可把 `docker-compose.yml` 里的 `ports: "5000:5000"` 删掉，
> 让 5000 只在容器网络内可达，仅由 Nginx 对外暴露 443，更安全。
> 若把 Nginx 也容器化，则把反代目标写成 `http://exam:5000`（compose 服务名）。

## 6. 日常运维

```bash
docker compose ps            # 状态
docker compose logs -f exam  # 实时日志
docker compose down          # 停止
docker compose up -d --build # 升级（拉新代码后）
```

## 7. 已知注意点

| 项 | 说明 |
|---|---|
| ClosedXML | 导出 Excel 用 ClosedXML 0.105.1，Dockerfile 已装 `libgdiplus` 作保险；正常导出无需它，若导出异常保留该依赖即可。 |
| SQLite 并发 | 单文件 SQLite 适合中小规模考试；高并发建议换 PostgreSQL（需改 `AppDbContext` 与连接串）。 |
| Swagger | `Program.cs` 始终启用 Swagger；生产若不希望暴露，可在 `ASPNETCORE_ENVIRONMENT=Production` 时按环境关闭（可选优化）。 |
| CORS | 当前 `AllowAnyOrigin`；上 HTTPS 后建议收紧为你的域名。 |
| JWT 密钥 | 默认 `appsettings.json` 里是 Dev 占位密钥，**必须**通过 `Jwt__Secret` 环境变量覆盖为强随机值。 |

## 8. 发布镜像到 Docker Hub（服务器直接 pull，免构建）

适合想把镜像托管到 Docker Hub、服务器上不再本地编译的场景。

### 8.1 本机（Windows，需 Docker Desktop）构建并推送
```powershell
cd F:\workbuddy\ExamSystem

# 1) 登录 Docker Hub（按提示输入用户名 / 密码，或 Access Token）
docker login

# 2) 构建 Linux 镜像（Docker Desktop 默认即 linux/amd64；服务器是 ARM 见 8.3）
docker build -t <你的DockerHub用户名>/examsystem:latest .

# 3) 推送到 Docker Hub
docker push <你的DockerHub用户名>/examsystem:latest
```
- 仓库不存在会自动创建（默认 **public**）。要私有：先在 Docker Hub 网页建 private 仓库再 push。
- 建议同时打版本标签便于回滚：
  ```powershell
  docker tag <你的DockerHub用户名>/examsystem:latest <你的DockerHub用户名>/examsystem:1.0.0
  docker push <你的DockerHub用户名>/examsystem:1.0.0
  ```

### 8.2 服务器上改为直接拉镜像（不再本地 build）
把 `docker-compose.yml` 里的 `build:` 整段删掉，只留 `image:`：
```yaml
services:
  exam:
    image: <你的DockerHub用户名>/examsystem:latest
    container_name: examsystem
    restart: unless-stopped
    ports:
      - "5000:5000"
    environment:
      - EXAM_DB=Data Source=/data/exam.db
      - ASPNETCORE_ENVIRONMENT=Production
      - Jwt__Secret=REPLACE_WITH_STRONG_RANDOM_SECRET
    volumes:
      - examdata:/data
volumes:
  examdata:
```
然后：`docker compose pull && docker compose up -d`。**私有仓库**需先在服务器 `docker login`。

### 8.3 多架构（服务器是 ARM64，如树莓派 / 阿里云 Graviton）
用 buildx 一次打多架构并直推：
```powershell
docker buildx create --use
docker buildx build --platform linux/amd64,linux/arm64 `
  -t <你的DockerHub用户名>/examsystem:latest --push .
```

### 8.4 注意事项
- 镜像不含 `exam.db`（`.dockerignore` 已排除），数据仍在服务器卷里，`pull` 升级不丢。
- 镜像里只含编译产物与 `appsettings.json` 的 Dev 占位 JWT 密钥；真正密钥由 `Jwt__Secret` 环境变量注入，**不会进镜像**，公开镜像也安全。
- 镜像体积主要来自 aspnet 运行时 + ClosedXML/libgdiplus，通常 200–400MB。

## 9. 国内网络注意（Docker Hub / mcr 拉不到怎么办）

若在中国大陆，下列现象很常见：`docker login` 或 build 拉 `mcr.microsoft.com` 时
`registry-1.docker.io/v2` 或 `mcr.microsoft.com` **连接超时**。即使用 PAT 登录也一样超时——
因为都打同一个境外地址。**没有代理时，本机走 Docker Hub 这条路走不通。**

而且 `docker build` 阶段必须能拉到 `mcr.microsoft.com/dotnet/sdk:8.0` 与 `aspnet:8.0`，
先确认这一跳通了，再谈 push。

### 三条出路
1. **代理 / VPN（最省事，若能搞到）**
   - Docker Desktop → Settings → Resources → Proxies，填 HTTP/HTTPS 代理，Apply & Restart。
   - build 拉 `mcr.microsoft.com` 也走该代理。
   - 通了之后按 §8 正常 `docker build` + `docker push` 到 Docker Hub。
   - 登录绕开网页：用 `docker login -u <用户名>`，密码填 Access Token。

2. **阿里云容器镜像服务 ACR（推荐，无需代理）**
   - 控制台建命名空间 + 仓库（个人版免费）。
   - 用「**云端构建**」：把源码（或 git 库）交给阿里云，由阿里云构建机拉 `mcr` 并产出镜像、
     存入你的 ACR 仓库；你本机/服务器只 `docker pull registry.cn-hangzhou.aliyuncs.com/<ns>/examsystem:latest`
     （全程在墙内，快）。
   - 登录：`docker login registry.cn-hangzhou.aliyuncs.com`（用阿里云账号）。
   - compose 里 `image:` 改成上述地址；私有仓库服务器也要先 `docker login`。

3. **直接在服务器 build（最简，可完全不用 Docker Hub）**
   - 把 `ExamSystem` 文件夹 scp 到 Linux 服务器，`docker compose up -d --build` 即可。
   - 前提是服务器能联网拉 `mcr.microsoft.com`；国内服务器可在
     `/etc/docker/daemon.json` 配 `registry-mirrors` 或用阿里云 mcr 加速源。

> 决策要点：你本机无代理 → 别耗在 Docker Hub 上。服务器若能联网就在服务器 build（路径 3）；
> 否则用阿里云 ACR 云端构建（路径 2），本机/服务器都只和阿里云打交道。
