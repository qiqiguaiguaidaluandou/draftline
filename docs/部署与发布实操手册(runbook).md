# 部署与发布实操手册（Runbook）

> 面向「实际操作」的流水手册，配合设计说明见 `开发文档-⑨部署与发布(Linux后端+ClickOnce客户端).md`。
> 本手册记录**真实生产环境的确切路径/命令**，以及本项目特有的坑（都是实战踩出来的）。

---

## 0. 环境与关键变量对照表

操作前先对号，全篇出现 `<后端IP>` 等占位处按此替换。

| 变量 | 实际值 | 说明 |
|---|---|---|
| **后端服务器用户** | `jptadmin` | SSH / systemd 运行身份 |
| **后端部署目录** | `/home/jptadmin/kqspace/draftline-all/draftline-gateway` | 后端程序所在，= systemd `WorkingDirectory` = ContentRoot |
| **ClickOnce 分发目录** | `/home/jptadmin/kqspace/draftline-all/clickonce` | 客户端包存放处，**与后端目录平级、解耦**（`appsettings.local.json` 的 `ClickOnce.DistPath` 绝对路径） |
| **访问前缀 RequestPath** | `/draftline` | URL 前缀，客户端从 `http://<后端IP>:8080/draftline/...` 安装 |
| **后端监听** | `http://0.0.0.0:8080`（直连）或 `http://localhost:8080`（挂 nginx） | 在 `appsettings.local.json` 的 `Urls` 里配 |
| **后端 dotnet** | `/usr/bin/dotnet`（系统级） | 框架依赖发布，用它拉起 `.dll` |
| **`<后端IP>`** | 待确认（客户端能访问到的地址） | 数据库那台是 `172.10.42.142`，后端对客户端暴露的 IP 以实际为准 |
| **Windows 构建机仓库** | 例：`C:\Users\108645\kqspace\draftline` | 出客户端包用，需装 VS 2022 或 .NET 8 SDK |

> ⚠️ **两条独立发布线**：后端（Linux）与客户端（Windows ClickOnce）各走各的。改后端只重部署服务器、客户端无感；改客户端只重发 ClickOnce、后端不动。

---

## 1. 后端首次部署（Linux）

### 1.1 发布程序

在能编译的机器上（或 CI）：

```bash
# 框架依赖发布（服务器已装 dotnet 运行时时用这个）
dotnet publish src/Draftline.Gateway/Draftline.Gateway.csproj -c Release \
  -o /home/jptadmin/kqspace/draftline-all/draftline-gateway
```

> 若某开发机 dotnet 不在 PATH（如装在 `~/.dotnet`），用绝对路径 `~/.dotnet/dotnet publish ...`。

### 1.2 配置 `appsettings.local.json`（含 3 个坑）

真实配置（数据库密码 / JWT / 管理员账号）都在 `appsettings.local.json`，**被 gitignore，不进仓库**。Web SDK 会把它 glob 进 publish 产物，但**干净检出/CI 上不存在**，务必确认它在部署目录里：

```bash
ls -l /home/jptadmin/kqspace/draftline-all/draftline-gateway/appsettings.local.json
# 没有就从源码拷过去
```

关键配置项：

```json
{
  "Urls": "http://0.0.0.0:8080",
  "ConnectionStrings": { "DefaultConnection": "Host=172.10.42.142;Port=5433;Database=tzhj_db;Username=postgres;Password=***" },
  "Jwt": { "Key": "***", "Issuer": "TZHJ.Gateway", "Audience": "TZHJ.App", "ExpiryMinutes": 480 },
  "Admin": { "EmployeeId": "admin", "Password": "***", "DisplayName": "系统管理员" },
  "ClickOnce": {
    "DistPath": "/home/jptadmin/kqspace/draftline-all/clickonce",
    "RequestPath": "/draftline"
  }
}
```

**三个坑：**

1. **监听地址**：`Urls` 写 `http://0.0.0.0:8080` 客户端才能从别的机器直连；写 `localhost` 只监听回环（仅在挂 nginx 反代时用）。
2. **`Urls` 会覆盖环境变量**：`Program.cs` 最后才加载 `appsettings.local.json`，它的 `Urls` 会**盖掉** `ASPNETCORE_URLS`。所以改监听地址**只改这个 json**，别指望设环境变量——systemd unit 里也不要设 `ASPNETCORE_URLS`（会误导）。
3. **`DistPath` 用绝对路径**：指向部署目录**外面**的 `.../draftline-all/clickonce`，与后端程序解耦，避免重部署时误删客户端包（详见 §5）。相对路径则相对 ContentRoot（部署目录）解析。

### 1.3 数据库迁移（首次必做）

代码**没有自动建表**（`Program.cs` 无 `Migrate()`）。首次部署或有新迁移时，在有 `dotnet-ef` 的机器上、用连到该库的连接串跑：

```bash
dotnet tool install --global dotnet-ef   # 装过跳过
dotnet ef database update --project src/Draftline.Gateway/Draftline.Gateway.csproj
```

> 漏了这步 → 客户端登录/取数时报 `relation "xxx" does not exist`。

### 1.4 用 systemd 常驻

创建 `/etc/systemd/system/draftline-gateway.service`：

```bash
sudo tee /etc/systemd/system/draftline-gateway.service > /dev/null <<'EOF'
[Unit]
Description=Draftline Gateway (ASP.NET Core backend)
After=network.target

[Service]
Type=simple
User=jptadmin
Group=jptadmin
WorkingDirectory=/home/jptadmin/kqspace/draftline-all/draftline-gateway
ExecStart=/usr/bin/dotnet /home/jptadmin/kqspace/draftline-all/draftline-gateway/Draftline.Gateway.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Restart=always
RestartSec=3
SyslogIdentifier=draftline-gateway

[Install]
WantedBy=multi-user.target
EOF
```

要点：
- `User=jptadmin`：程序/目录归它所有，换别的用户会因权限起不来。
- `WorkingDirectory` **必须**是部署目录，否则 `App_Data`、`logs`、相对 `DistPath`、`appsettings.local.json` 等相对路径全解析错。
- **不设** `ASPNETCORE_URLS`（见 §1.2 坑 2）。
- 若改用自包含发布，`ExecStart` 改成可执行文件本身 `.../draftline-gateway/Draftline.Gateway`，且不需要 dotnet。

### 1.5 启动 + 开机自启 + 验证

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now draftline-gateway
sudo systemctl status draftline-gateway          # active (running) 即成功

sudo journalctl -u draftline-gateway -f          # 应出现 "Now listening on: http://0.0.0.0:8080"
curl -i http://localhost:8080/                   # 本机自测
# 换 <后端IP> 从别的机器 curl，测对外连通（防火墙/安全组需放 8080）
```

---

## 2. 客户端首次发布（Windows）

> ClickOnce 包**只能在 Windows 出**，Linux 会报 `MSB3096 GenerateLauncher only supported on Windows`。

### 2.1 前提

- Windows 机装 **Visual Studio 2022**（含「.NET 桌面开发」工作负载）或至少 .NET 8 SDK。
- 项目已声明 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`（已在 `Draftline.App.csproj`，修复 VS 图形发布的 NETSDK1047，见 §7）。

### 2.2 改两处地址（会烤进包，发布前必须定死）

1. `src\Draftline.App\Properties\PublishProfiles\ClickOnceProfile.pubxml`：
   ```xml
   <PublishUrl>http://<后端IP>:8080/draftline/</PublishUrl>
   <InstallUrl>http://<后端IP>:8080/draftline/</InstallUrl>
   ```
2. `src\Draftline.App\appsettings.json`（客户端连后端的基址）：
   ```json
   "Http": { "BaseUrl": "http://<后端IP>:8080", "TimeoutSeconds": 60 }
   ```

> 这些地址烤进部署清单，**换服务器/上 HTTPS 域名要改这里重发包**。

### 2.3 清理旧产物（切换配置后必做一次）

加过 RID、或切换过 Debug/Release 后，旧 `obj` 会导致 WPF baml 找错目录（如 `找不到 FluentTheme.baml`）。发布前先清：

```powershell
Remove-Item -Recurse -Force src\Draftline.App\bin, src\Draftline.App\obj -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force src\Draftline.Core\bin, src\Draftline.Core\obj -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force src\Draftline.Infrastructure\bin, src\Draftline.Infrastructure\obj -ErrorAction SilentlyContinue
```

### 2.4 发布（推荐命令行 msbuild）

开 **「Developer PowerShell for VS 2022」**，仓库根目录跑：

```powershell
msbuild src\Draftline.App\Draftline.App.csproj "/t:Restore;Publish" /p:PublishProfile=ClickOnceProfile /p:Configuration=Release /v:m
```

> - **含分号的参数必须带双引号** `"/t:Restore;Publish"`，否则 PowerShell 把 `Publish` 当独立命令报错（cmd 的 Developer Command Prompt 不用引号）。
> - **必须带 `Restore` 且和 `Publish` 同一次调用**，否则还原不带 `win-x64` → NETSDK1047。
> - **为什么用命令行而非 VS 图形界面**：VS 会吞掉发布错误（错误列表里看不到），命令行直接把 `error` 打出来；且可重复、可脚本化。VS 图形发布（右键项目→发布→选 Profile）在 RID 修复后也能用，留作开发调试用。

产物在 `src\Draftline.App\bin\publish\`：
```
publish\
├── setup.exe                    ← 引导安装器（按需装 .NET 8 桌面运行时）
├── Draftline.App.application    ← 部署清单（记录最新版本号+位置）
└── Application Files\Draftline.App_1_0_0_x\...
```

### 2.5 上传到分发点

把 `publish\` 里的**内容**传到 **`DistPath` 指的目录**（= `/home/jptadmin/kqspace/draftline-all/clickonce`，**不是**后端目录下的 clickonce！）：

```powershell
# 方式 A：命令行 scp（Win10/11 自带）
ssh jptadmin@<后端IP> "mkdir -p /home/jptadmin/kqspace/draftline-all/clickonce"
cd C:\Users\108645\kqspace\draftline\src\Draftline.App\bin\publish
scp -r * jptadmin@<后端IP>:/home/jptadmin/kqspace/draftline-all/clickonce/
```

> - 先 `cd` 进 publish 再 `scp -r *`，能正确带过去含空格的 `Application Files` 目录。
> - 不要多套一层 `publish\`（别传成 `clickonce/publish/setup.exe`）。
> - **方式 B**：用 WinSCP 图形拖拽（SFTP，主机 `<后端IP>`、用户 `jptadmin`），把 publish 内容拖到 `clickonce/`。

### 2.6 验证 + 安装

后端确认文件到位（放对目录）：
```bash
ls /home/jptadmin/kqspace/draftline-all/clickonce/
# 应看到 setup.exe  Draftline.App.application  'Application Files'
```

浏览器验证分发点（任意能连后端的机器）：
```
http://<后端IP>:8080/draftline/Draftline.App.application
```
能下到 XML → 通了 ✅（404 多半是改 `DistPath` 后**后端没重启**，`sudo systemctl restart draftline-gateway` 再试）。

客户端机安装：浏览器开 `http://<后端IP>:8080/draftline/setup.exe` → 运行 → 缺运行时自动装 → 桌面/开始菜单出现快捷方式 → 打开，用管理员账号（`admin` + 配的密码）登录，验证取数正常。

---

## 3. 日常发版：后端改动后重新部署

**改了后端（含 Core/Infra 中后端用到的部分）：**

```bash
# 1. 重新发布（覆盖，不要 rm -rf 整个目录，见 §5）
dotnet publish src/Draftline.Gateway/Draftline.Gateway.csproj -c Release \
  -o /home/jptadmin/kqspace/draftline-all/draftline-gateway

# 2. 有新迁移就跑（无则跳过）
dotnet ef database update --project src/Draftline.Gateway/Draftline.Gateway.csproj

# 3. 重启
sudo systemctl restart draftline-gateway
sudo systemctl status draftline-gateway
```

**客户端完全不用动，全员即时生效。**

> 重发后确认 `appsettings.local.json` 仍在部署目录（Web SDK 一般会带上，但值得瞄一眼）。

---

## 4. 日常发版：客户端改动后重新发布

**改了客户端（含 Core/Infra 中客户端用到的部分）：**

1. **版本号 +1**（关键！）——`ClickOnceProfile.pubxml` 里 `ApplicationVersion` 末位递增：
   ```xml
   <ApplicationVersion>1.0.0.1</ApplicationVersion>   <!-- 上次 1.0.0.0 → 本次 1.0.0.1 -->
   ```
   > 这是客户端识别「有新版」的**唯一依据**，不加则永远不更新。
2. 清 obj/bin（§2.3）。
3. 命令行发布（§2.4）。
4. 新 `publish\` 内容**覆盖上传**到 `clickonce/`（§2.5）。**保留旧版本文件夹别删**——只新增新版本目录 + 覆盖顶层 `.application`。
5. 用户下次从快捷方式启动 → 自动检测新版 → 先下载安装再进入，弹 Toast「已更新至 vX」。

**共享库 Core/Infrastructure 改动**：影响客户端 → 重发 ClickOnce；影响后端 → 重部署后端；两边都用到 → 两边各做一次。

---

## 5. ⚠️ 重部署安全须知（务必读）

客户端包（`clickonce/`）与后端程序**已解耦**（`DistPath` 用了部署目录外的绝对路径），但仍需注意：

- 重部署后端用**普通 `dotnet publish -o <目录>`**（覆盖式）→ 安全，不会碰 `clickonce/`、也不会删 `appsettings.local.json`（Web SDK 会重新带上）。
- **绝不要**用 `rm -rf <部署目录>/*` 再 publish，或 `rsync --delete` —— 会连带删掉不属于本次产物的文件。
- `clickonce/` 在 `.../draftline-all/` 下、与后端目录平级，正常重部署碰不到它。

---

## 6. 发版检查清单（Cheat Sheet）

**后端发版：**
- [ ] `dotnet publish -o 部署目录`
- [ ] `appsettings.local.json` 在位、`Urls`/连接串正确
- [ ] 有迁移则 `ef database update`
- [ ] `systemctl restart draftline-gateway` → `status` 为 running
- [ ] `curl http://<后端IP>:8080/` 通

**客户端发版：**
- [ ] `ApplicationVersion` 末位 +1
- [ ] pubxml `InstallUrl` / appsettings.json `BaseUrl` 指向对的后端地址
- [ ] 清 obj/bin
- [ ] `msbuild "/t:Restore;Publish" ...` 成功
- [ ] `publish\` 内容上传到 `DistPath` 目录（不是后端目录下）
- [ ] `http://<后端IP>:8080/draftline/Draftline.App.application` 能下到 XML
- [ ] 客户端 `setup.exe` 安装、登录、取数验证

---

## 7. 常见错误速查（本项目实战）

| 现象 | 原因 | 解决 |
|---|---|---|
| `MSB3096 GenerateLauncher only supported on Windows` | 在 Linux 上发 ClickOnce | 换 Windows 机发布 |
| `MSB3964 找不到 Launcher.exe` | 用 `dotnet publish` CLI 发 ClickOnce，缺 Launcher 工具链 | 用 VS 或 VS 的 `msbuild` 发布（§2.4） |
| `NETSDK1047 ...没有 net8.0-windows/win-x64 的目标` | 还原没带 RID | csproj 已加 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`；命令行用 `"/t:Restore;Publish"` 同一次带 RID 还原 |
| `找不到 ...\Styles\FluentTheme.baml`（路径缺 win-x64 层） | 加 RID 前后旧 obj 残留混淆 WPF baml | 删 bin/obj 重发（§2.3） |
| PowerShell 报 `无法将"Publish"识别为 cmdlet` | `;` 被当命令分隔符 | 参数加双引号 `"/t:Restore;Publish"` |
| 浏览器访问 `.application` 404 | 文件没在 `DistPath` 目录 / 改配置后没重启后端 | 确认文件在 `DistPath`（§0）；`systemctl restart` |
| 传了文件后端却读不到 | 传到了错目录（如后端目录下 clickonce，而 `DistPath` 指别处） | 以 `appsettings.local.json` 的 `DistPath` 为准（本项目是 `.../draftline-all/clickonce`） |
| 客户端登录/取数报 `relation does not exist` | 数据库表没建 | 跑 `ef database update`（§1.3） |
| `ssh: connect ... port 22: Connection timed out` | IP 错 / 22 未开 / 需 VPN | 核对 IP（注意别把 `42.142` 写成 `142.42`）、`Test-NetConnection <IP> -Port 22`、确认登录方式 |
| 改了 `Urls` 环境变量却不生效 | `appsettings.local.json` 最后加载会覆盖它 | 改 `appsettings.local.json` 的 `Urls`（§1.2） |
| VS 发布失败但错误列表无 error | VS 吞掉了 ClickOnce 发布错误 | 改用命令行 msbuild 看真实报错（§2.4） |

---

## 8. 附注：SDK 版本

构建机若装了多个 SDK（如同时有 .NET 10 与 .NET 8），发布可能默认走高版本。ClickOnce 对 SDK 版本敏感，若出现诡异错误，可在仓库根加 `global.json` 钉到 .NET 8：

```json
{ "sdk": { "version": "8.0.xxx", "rollForward": "latestFeature" } }
```

（`8.0.xxx` 取 `dotnet --list-sdks` 里实际的 8.x 版本。）目前未强制，按需启用。
