# 012 Blazor 管理后台（用户/角色/权限/日志）

- 状态：已完成（编译/测试通过；Blazor 交互 UI 与 Cookie 登录链路待真机浏览器验证）
- 日期：2026-06-22
- 关联提交：<git 短哈希>（提交时填）

## 需求 / 改动

此前管理员维护用户、角色、权限只能用命令行（curl），查日志要直接进数据库——既易错又不安全（等于人人要 DB 口令）。需要一个统一的图形化后台。

决策（与用户确认）：
- 形式用 **Blazor Server，托管在网关进程内**（一个部署包，无 npm/前端构建链，留在 .NET 技术栈）。
- 用 **Cookie 鉴权** 守卫后台（与 `/api/*` 的 JWT 体系相互独立）；登录复用 `IAuthService` 校验凭证，并额外要求"启用中的管理员"。
- 管理操作抽到 **共享 `IAdminService`**，被 REST 端点(`/api/admin/*`) 与 Blazor 页面共用，逻辑零重复。

## 方案

1. **共享服务 `IAdminService`/`AdminService`**：用户（列/建/重置密码/启停用/挂角色）、角色（列/建/改/删）、操作日志（筛选分页）、可选组。`/api/admin/*` 端点改为薄封装调它；Blazor 页面也调它。
2. **新增后端端点**：`GET /api/admin/logs`（按 工号/动作/状态/时间 筛选 + 分页）、`GET /api/admin/groups`（现有批次里出现过的 流程+组，配角色时下拉防打错）。
3. **网关托管 Blazor**：`AddRazorComponents().AddInteractiveServerComponents()` + `MapRazorComponents<App>().AddInteractiveServerRenderMode()`；`UseStaticFiles/UseAuthentication/UseAuthorization/UseAntiforgery`。
4. **Cookie 登录**：`/admin/login`（表单 POST，`SignInAsync` 写 Cookie，声明 `draftline:isAdmin`）、`/admin/logout`（`SignOutAsync`）；策略 `AdminOnly` 要求该声明。登录失败/非管理员记 `AdminLogin` 审计。
5. **页面**（`/admin/*`，`@rendermode InteractiveServer`，`[Authorize(Policy=AdminOnly)]`）：登录、用户管理、角色管理、操作日志；侧边导航布局；未授权重定向登录。
6. EF 上下文/DbContext 用法：Blazor 页面每次操作 `IServiceScopeFactory.CreateScope()` 取新 `IAdminService`（避免长生命周期 DbContext 并发问题）。

取舍：
- 业务失败统一走 `200 + ApiResult.Success=false`（与 login/change-password 一致），不再用 4xx 区分——简化前端处理。
- Cookie 鉴权独立于 JWT：后台用 Cookie，WPF 客户端继续用 JWT，互不影响（`/api/*` 仍走 `TokenEndpointFilter`，不经 ASP.NET 认证中间件）。
- 仍是 Blazor Server（非 WASM）：无需 wasm 工作负载，单进程、LAN 内部工具足够。

## 实现

改动文件：

**Server（新增）** — `Auth/AdminService.cs`（`IAdminService` + 实现，集中全部管理逻辑）、`Endpoints/AdminAuthEndpoints.cs`（`/admin/login`、`/admin/logout`）、`Components/`：`_Imports.razor`、`App.razor`（根文档 + 内联样式）、`Routes.razor`、`RedirectToLogin.razor`、`Layout/AdminLayout.razor`、`Layout/BlankLayout.razor`、`Pages/AdminLogin.razor`、`Pages/AdminUsers.razor`、`Pages/AdminRoles.razor`、`Pages/AdminLogs.razor`。

**Server（改）** — `Endpoints/ApiEndpoints.cs`（`/api/admin/*` 改为调 `IAdminService` 的薄封装；新增 `/logs`、`/groups`；删除内联的 `LogAdmin`/`BuildRolePermissions`，保留 `TryParseDt`）；`Program.cs`（注册 `IAdminService`、Blazor、Cookie 鉴权、`AdminOnly` 策略；管线加静态文件/认证/授权/防伪 + `MapRazorComponents` + `MapAdminAuthEndpoints`）。

**Core 契约** — `Contracts/Http/HttpDtos.cs`：新增 `AdminLogEntry`、`AdminLogListResponse`、`GroupOption`。

**测试** — `tests/Draftline.Tests/Auth/AdminServiceTests.cs`：建用户（强制改密/查重/弱口令）、角色建+挂载并体现在有效权限、未知角色拒绝、角色重名拒绝、删角色级联清挂载、禁停用自己、日志筛选+分页。

## 验证

- ✅ `dotnet build Draftline.sln -p:EnableWindowsTargeting=true` → **0 Error**（含全部 Razor 组件编译）。
- ✅ `dotnet test tests/Draftline.Tests` → **59 Passed / 0 Failed**（含 8 个新增 AdminService 用例）。
- ✅ 启动冒烟（沙箱无 PostgreSQL）：网关 DI 容器构建成功、DataProtection 初始化（Cookie/Blazor 依赖）、进入服务启动阶段——证明 Blazor 宿主/Cookie 鉴权/中间件注册无误；随后因 `DataIngestionService` 连不上库而中止（既有行为：网关启动即需可达的 PostgreSQL，与本次改动无关）。
- 待真机浏览器验证（沙箱无 GUI/无库，无法跑）：登录 Cookie 往返、各页交互渲染、增删改实际落库。验证方法：配好库与 `Jwt`/`Admin` 后启动网关，浏览器开 `http://<网关>:8080/admin/login`，用引导管理员登录。

## 备注 / 待办

- 管理后台入口：`/admin/login`（仅启用中的管理员可进）。访问 `http://<网关IP>:8080/admin`。
- HTTPS：Cookie 后台与登录走明文 HTTP 时凭证可被嗅探——上线务必置于 HTTPS（反代或 Kestrel 证书）之后；可给 Cookie 设 `Secure`。
- `DataIngestionService` 启动即强依赖 DB（连不上则 host 崩）——非本次引入；如需"无库也能起后台"，可单独把它改成容错启动。
- 暂未做：管理后台内的"导出日志 CSV""分页跳页""角色批量操作"等增强，按需再加。
