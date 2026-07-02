# 开发文档-⑨ 部署与发布（Linux 后端 + ClickOnce 客户端）

## 1. 概述

本项目是「Linux 服务器跑后端 + Windows 桌面客户端」的组合，两者是**两条独立的发布线**：

- **后端 `TZHJ.Gateway`**（ASP.NET Core，`net8.0`）——跑在 Linux 服务器上，原生支持。改后端只需重新部署服务器，**所有客户端即时生效、无需动客户端**。
- **客户端 `TZHJ.App`**（WPF，`net8.0-windows`）——通过 **ClickOnce** 分发到操作员 Windows 电脑，靠版本号自动升级。

> **硬约束（已实测）**：ClickOnce 客户端包**必须在 Windows 上生成**，Linux 无法产出。
> 在 Linux 上执行 `dotnet publish ... -p:PublishProfile=ClickOnceProfile` 会报：
> `error MSB3096: Task "GenerateLauncher" is only supported when building on Windows.`
> 因此需要一台 Windows 构建环境（实体机 / Windows 虚机 / GitHub Actions `windows-latest`）。

### 三个角色

```
┌─────────────────────┐        ┌──────────────────────────┐
│  Windows 构建机       │        │   Linux 服务器            │
│ (开发PC / Win VM /    │        │  ① 跑后端 TZHJ.Gateway    │
│  GitHub Win runner)  │        │  ② 当 ClickOnce 分发点     │
│  dotnet publish      │        │     (nginx serve 静态文件) │
│  ↓ 出 ClickOnce 包    │        └──────────┬───────────────┘
│  上传到 Linux 分发点 ──┼──────────────────►│ HTTPS
└─────────────────────┘                   │  用户从这里装/自动更新
                                   ┌────────▼─────────┐
                                   │ 用户 Windows 电脑  │
                                   │  装 TZHJ.App 客户端 │
                                   │  HTTP 调后端接口     │
                                   └──────────────────┘
```

一台 Linux 同时干「后端」和「分发点」两件事。

---

## 2. 后端上线（Linux）

`TZHJ.Gateway` 是标准 ASP.NET Core，Linux 原生支持。

### 2.1 发布

在 Linux 服务器（或 CI）上：

```bash
# 框架依赖发布（需服务器装 aspnetcore-runtime-8.0）
dotnet publish src/TZHJ.Gateway/TZHJ.Gateway.csproj -c Release -o /opt/tzhj-gateway
```

若不想在服务器装运行时，改为自包含：

```bash
dotnet publish src/TZHJ.Gateway/TZHJ.Gateway.csproj -c Release \
  -r linux-x64 --self-contained true -o /opt/tzhj-gateway
```

（数据库连接等按 `docs/开发文档-⑤` 配好；后端与 PostgreSQL 的对接不在本文范围。）

### 2.2 用 systemd 常驻

`/etc/systemd/system/tzhj-gateway.service`：

```ini
[Unit]
Description=TZHJ Gateway
After=network.target

[Service]
WorkingDirectory=/opt/tzhj-gateway
ExecStart=/usr/bin/dotnet /opt/tzhj-gateway/TZHJ.Gateway.dll
Environment=ASPNETCORE_URLS=http://127.0.0.1:5080
Environment=ASPNETCORE_ENVIRONMENT=Production
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now tzhj-gateway
sudo systemctl status tzhj-gateway
```

（自包含发布时把 `ExecStart` 改为 `/opt/tzhj-gateway/TZHJ.Gateway` 可执行文件本身。）

### 2.3 nginx 反代 + HTTPS

见 §4，后端反代与 ClickOnce 分发点合在同一个 nginx server 里。

---

## 3. 出客户端包（Windows 必需）

产物**不是单个 .exe**，而是一整套 ClickOnce 发布物（清单 + 应用文件 + `setup.exe`）。

### 3.1 发布前必改 `pubxml`

`src/TZHJ.App/Properties/PublishProfiles/ClickOnceProfile.pubxml` 的两处占位（URL 会被**写进部署清单**，故必须发布前定好）：

```xml
<PublishUrl>https://下载域名/tzhj/</PublishUrl>
<InstallUrl>https://下载域名/tzhj/</InstallUrl>
```

指向 §4 的 Linux 分发点 URL。

- 想免用户装 .NET 桌面运行时（内网批量部署更省事）：把 `SelfContained` 改 `true`（包更大）。
- 每次发版**递增 `ApplicationVersion` 末位**（如 `1.0.0.0` → `1.0.0.1`）——这是客户端识别新版的唯一依据，不递增则永远不更新。

### 3.2 在 Windows 上发布

```powershell
dotnet publish src\TZHJ.App\TZHJ.App.csproj -p:PublishProfile=ClickOnceProfile
```

（或 Visual Studio 右键 `TZHJ.App` → 发布 → 选 `ClickOnceProfile`。）

### 3.3 产物结构

输出在 `src\TZHJ.App\bin\publish\`：

```
publish\
├── setup.exe                    ← 引导安装程序（按需装 .NET 8 桌面运行时）
├── TZHJ.App.application          ← 部署清单（记录"最新版本号+位置"，更新判据核心）
└── Application Files\
    └── TZHJ.App_1_0_0_1\         ← 本版全部文件
        ├── TZHJ.App.dll.deploy   ← 客户端主程序（.deploy 后缀是 ClickOnce 默认）
        ├── TZHJ.Core.dll.deploy
        ├── TZHJ.Infrastructure.dll.deploy
        ├── appsettings.json.deploy
        └── TZHJ.App.application  ← 本版应用清单
```

`.deploy` 后缀（`MapFileExtensions` 默认开）让 Web 服务器不因未知扩展名拦下载。

> **客户端连后端的地址**在 `appsettings.json` 的 `Http:GatewayBaseUrl`，会随包一起发出去；发布前确认它指向 §4 的后端 HTTPS 地址。

---

## 4. Linux 分发点（nginx 发静态文件）

把 Windows 出的 `publish\` **整个目录原样上传**到 Linux（如 `/var/www/tzhj/`），nginx 当静态文件发。**关键是 MIME 类型**，否则客户端认不出清单。

```nginx
server {
    listen 443 ssl;
    server_name 下载域名;
    # ssl_certificate / ssl_certificate_key ...

    # ① ClickOnce 分发点
    location /tzhj/ {
        root /var/www;
        types {
            application/x-ms-application  application;
            application/x-ms-manifest     manifest;
            application/octet-stream      deploy;
        }
        default_type application/octet-stream;   # setup.exe / .deploy
    }

    # ② 后端接口反代到 systemd 的 kestrel
    location /api/ {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

上传可用 `scp -r publish/* user@server:/var/www/tzhj/` 或 CI 自动推送（见 §6）。

---

## 5. 用户端安装与更新（全自动）

### 5.1 首次安装（每个操作员一次）

1. 浏览器打开 `https://下载域名/tzhj/` → 点 `setup.exe`（或 `TZHJ.App.application`）。
2. `setup.exe` 检测到未装 .NET 8 桌面运行时 → 自动下载安装（`BootstrapperEnabled=true`）。
3. ClickOnce 装到用户目录 + 建开始菜单/桌面快捷方式（`Install=true` / `CreateDesktopShortcut=true`）。

### 5.2 后续更新（Foreground 逻辑）

- 用户每次**从快捷方式启动** → 客户端联网读分发点 `TZHJ.App.application` → 版本比本地高就**先下载安装再启动**（同步，启动前完成）。
- `UpdateRequired=false`：更新下载失败（如断网）时允许本次先用旧版进去干活，不卡死。
- 更新后首次运行弹 Toast「客户端已更新至 vX」（`App.xaml.cs` + `UpdateService`）。

### 5.3 设置页「检查更新」按钮

.NET 8 已移除程序内主动拉取更新的 API，故此按钮**不自己下载更新**，而是「确认后重启程序」以再走一次 §5.2 的启动检查。四个分支见 `SettingsViewModel.CheckUpdate()`（对应 `changes/020`）。

---

## 6. 每次发新版的循环

| 你改了 | 操作 |
|---|---|
| **后端** | Linux 上重新 `dotnet publish` Gateway → `sudo systemctl restart tzhj-gateway`。**客户端不用动**，全员即时生效 |
| **客户端**（或 Core/Infra 里客户端用到的部分） | ① Windows 上 `ApplicationVersion` +1 → ② `dotnet publish` ClickOnce → ③ 新 `publish\` 覆盖上传到 `/var/www/tzhj/`。用户下次启动自动更新 |
| **共享库 Core/Infrastructure** | 影响客户端 → 重发 ClickOnce；影响后端 → 重部署后端；两边都用到 → 两边各做一次 |

---

## 7. （可选）用 GitHub Actions 出客户端包

没有实体 Windows 时，用 `windows-latest` runner 自动出包并推到 Linux 分发点。`.github/workflows/release-client.yml`：

```yaml
name: release-client
on:
  workflow_dispatch:
    inputs:
      version:
        description: ApplicationVersion (如 1.0.0.5)
        required: true

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Publish ClickOnce
        run: >
          dotnet publish src/TZHJ.App/TZHJ.App.csproj
          -p:PublishProfile=ClickOnceProfile
          -p:ApplicationVersion=${{ github.event.inputs.version }}
      - name: Upload to Linux 分发点
        shell: bash
        run: |
          echo "${{ secrets.DEPLOY_SSH_KEY }}" > key && chmod 600 key
          scp -i key -o StrictHostKeyChecking=no -r \
            src/TZHJ.App/bin/publish/* \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }}:/var/www/tzhj/
```

需在仓库 Secrets 配 `DEPLOY_SSH_KEY` / `DEPLOY_USER` / `DEPLOY_HOST`。

---

## 8. 待决 / 待办

- [ ] 确定下载域名 + HTTPS 证书，回填 `pubxml` 的 `PublishUrl` / `InstallUrl`（当前仍是占位 `\\YOUR-SERVER\TZHJ\`）。
- [ ] 决定 `SelfContained` 是否改 `true`（免用户装运行时）。
- [ ] 首次在 Windows 上实际发布一次，端到端验证安装 + 自动更新（Linux 无法验证运行期 ClickOnce 行为）。
- [ ] 对应路线图 `docs/路线图.md` 的「D4 ClickOnce 发布/自动更新」——客户端侧逻辑已完成，剩发布地址与实际发布。
```
