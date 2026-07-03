# 021 后端托管 ClickOnce 分发

- 状态：已完成
- 日期：2026-07-02
- 关联提交：8f9c43f

## 需求 / 改动

客户端 ClickOnce 包原方案需另装 nginx 当分发点（`开发文档-⑨` §4）。但生产由基础设施把域名转发到后端端口、测试直接 IP:端口，反代已不需自建 nginx。若再让后端直接把 ClickOnce 发布物当静态文件发出去，就能**彻底免 nginx**：安装与自动更新和 API 走同一 host/端口/域名。

## 方案

在 `Draftline.Gateway` 增加一段静态文件托管，指向发布物目录，并补上 ClickOnce 专属 MIME（`.application` / `.manifest` / `.deploy`），否则客户端认不出部署清单、下载被拦。挂载在鉴权中间件**之前**，使安装包公开可下（用户装客户端时尚未登录）。路径可配、可被 `appsettings.local.json` 覆盖。

## 实现

- 新增 `src/Draftline.Gateway/ClickOnce/ClickOnceOptions.cs`：`DistPath`（发布物目录，默认 `clickonce`，相对内容根）、`RequestPath`（对外前缀，默认 `/draftline`）。
- 新增 `src/Draftline.Gateway/ClickOnce/ClickOnceDistribution.cs`：`UseClickOnceDistribution` 扩展——`PhysicalFileProvider` + `FileExtensionContentTypeProvider`（补三个 MIME）+ `ServeUnknownFileTypes`；目录不存在则 `Directory.CreateDirectory`。
- `Program.cs`：绑定 `ClickOnce` 段并注册；在 `UseStaticFiles()` 之后、`UseAuthentication()` 之前调用 `UseClickOnceDistribution`。
- `appsettings.json` 增 `ClickOnce` 段（模板默认值；真实覆盖走 `appsettings.local.json`）。
- `.gitignore` 忽略运行时分发目录 `clickonce/`。
- `开发文档-⑨` §4 改为「方式 A 后端自托管（推荐，免 nginx）/ 方式 B nginx」，§6 上传目标同步。

## 验证

- `Draftline.Gateway` 编译：**成功，0 error**（2 个既有无关警告：NPOI EULA、PgOperationLogStore 可空）。
- 中间件逻辑用等价的独立最小 Web 应用实测（绕开本机缺 `appsettings.local.json` 导致的 DB/EBS 启动问题）：
  - `.application` → `application/x-ms-application` ✅
  - `.manifest` → `application/x-ms-manifest` ✅
  - `.deploy` → `application/octet-stream` ✅
  - `setup.exe` → `application/vnd.microsoft.portable-executable`（浏览器正常下载）✅
  - 不存在文件 → 404 ✅
- 整套 Gateway 在开发沙箱起不来，因只有模板 `appsettings.json`（8080 被占、EBS 地址为空等），非本改动问题。

## 备注 / 待办

- 部署时把 Windows 出的 `publish\` 拷进后端 `clickonce/` 即可；`pubxml` 的 `InstallUrl` 须与实际访问 URL 一致（测试 IP:端口 / 生产域名，换环境需重出包）。
