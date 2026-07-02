# 020 ClickOnce 检查更新落地

- 状态：已完成
- 日期：2026-07-02
- 关联提交：76ad7f1

## 需求 / 改动

设置页「检查更新」原为骨架占位（`SettingsViewModel.CheckUpdate()` 仅弹一句「骨架占位，发布后启用」）。需落地为可用逻辑，对接 ClickOnce 自动更新（路线图 D4 客户端侧）。

## 方案

关键约束：**.NET 8 已移除 `System.Deployment.Application` 的方法**（`CheckForUpdate` / `Update`），无法在程序内主动拉取并安装更新。实际自动更新走 `UpdateMode=Foreground`——管理员发布新版后客户端下次启动即同步检查并自动应用；启动器仅通过 `ClickOnce_*` 环境变量把只读属性共享给应用。

据此分两条路：

- **启动自动更新**：全靠 pubxml 的 Foreground 配置，无需代码。
- **设置页「检查更新」按钮**：只用官方支持的只读属性——读版本 / 判断是否经部署 / 触发重启以再走一次启动检查。不碰被删除的 API，最稳。

参考：<https://learn.microsoft.com/visualstudio/deployment/access-clickonce-deployment-properties-dotnet>

## 实现

- 新增 `src/TZHJ.App/Services/UpdateService.cs`：
  - `IUpdateService` / `UpdateService`，封装 `ClickOnce_IsNetworkDeployed` / `CurrentVersion` / `UpdatedVersion` / `IsFirstRun` / `UpdateLocation` / `ActivationUri`。
  - `GetStatus()` 读只读状态（不联网）；`RestartForUpdate()` 启动部署清单地址触发前台更新后关闭当前实例。
- 改 `src/TZHJ.App/ViewModels/SettingsViewModel.cs`：`CheckUpdate()` 换成四分支——未部署（开发/直跑）提示无更新通道；本次刚更新过报「已更新至 vX」；无重激活地址提示手动重启；正常则确认后重启触发更新。构造注入 `IUpdateService`。
- 改 `src/TZHJ.App/App.xaml.cs`：注册 `IUpdateService`；登录后若为安装/更新后首次运行（`IsFirstRun`），Toast 提示「客户端已更新至 vX」。

部署/发布整体流程另见 `docs/开发文档-⑨部署与发布(Linux后端+ClickOnce客户端).md`。

## 验证

- Linux 上受 WPF 平台限制，需加 `-p:EnableWindowsTargeting=true` 才能编译；已如此编译 `TZHJ.App`：**成功，0 warning 0 error**。
- ClickOnce 运行期行为（`ClickOnce_*` 环境变量、自动更新、重启触发）**无法在 Linux 验证**，需在 Windows 上实际发布安装后端到端验证。

## 备注 / 待办

- `pubxml` 的 `PublishUrl` / `InstallUrl` 仍是占位符，未实际发布。
- 社区库（如 AutoUpdateClickOnce）声称把主动更新能力带回 .NET 8，属非官方，本次未采用。
