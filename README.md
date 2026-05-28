# 图纸核价 / 挑图纸系统 — PC 客户端骨架

WPF 桌面客户端的工程骨架。当前**无外部接口**（EBS / PLM / SRM / DHR 尚未提供），故采用
**"接口收到网关后面 + Mock 顶上 + 本地即状态"** 的打法先行开发 UI：客户端只依赖一组网关契约接口，
真接口到位后**只换实现、UI 不动**。

> 字段口径以 `docs/方案设计.md` 为准（核价待填列 = **目标价**；挑图待填列 = **是否机加中心可以做**）。
> 完整设计、上线路线图、界面预览均在 `docs/` 下（`方案设计.md` / `路线图.md` / `WPF界面预览.html`）。
> 早期 HTML 原型 `界面原型_v2.html` 已废弃，不作参考。

## 工程结构

```
TZHJ.sln
src/
  TZHJ.Core/            领域模型 + 网关契约 + 字段 schema + 本地路径/批次窗口规则（跨平台，无 WPF）
    Enums/              FlowType / RowStatus / BatchLocation / FieldSource / FieldEditor
    Models/             Batch / MaterialRow / FieldDefinition / CollectionWindow / ClientConfig / LocalPaths ...
    Contracts/          IAuthGateway / IConfigGateway / IDataGateway / ISubmitGateway / ILocalBatchStore / DTOs
    Schemas/            FieldSchemas（核价6列/挑图16列首批字段）、CollectionSchedules（核价2窗/挑图3窗）
  TZHJ.Infrastructure/  契约实现（跨平台，无 WPF）
    Gateways/Mock/      MockAuth / MockConfig / MockData（造数+占位图纸）/ MockSubmit / DefaultFieldProvider
    Storage/            LocalBatchStore（落本地/读写xlsx/移目录/异常池/完整性校验）、ExcelGridIO、BatchManifest
    DependencyInjection.cs   AddTzhjMockInfrastructure(...)  ← 无接口先开发的总开关
  TZHJ.SampleData/      样例数据生成器（控制台，跨平台）：铺出完整本地文件夹树供 UI 开发对着用
  TZHJ.App/             WPF 客户端（net8.0-windows，仅此工程需 Windows）：MVVM + DI
    Services/  ViewModels/  Views/  Converters/
    Styles/FluentTheme.xaml   克制 Fluent 商务风主题（蓝 #2563EB；按钮/输入/DataGrid/卡片/导航样式）
    依赖 FluentIcons.Wpf（矢量 Fluent 图标，随程序打包，跨 Win10/11 一致）
```

### 三类工作（为什么 UI 大部分不依赖接口）

因"本地即状态、软件视图 = 本地文件夹视图"，依赖外部的只有 3 个边界点：

- **A 完全不依赖接口**：批次列表（映射文件夹）、可编辑网格、行/批次状态机、提交闸门、暂存、
  异常池、图纸有无标识与完整性校验、"在资源管理器中打开"。→ 已实现，对着样例数据即可开发。
- **B 依赖接口但 Mock 顶住**：`IAuthGateway`（登录）、`IDataGateway`（取数）、`ISubmitGateway`（回传）、
  `IConfigGateway`（配置/时间窗/字段下发）。→ 已有 Mock 实现，含可配延迟与随机失败用于调边界态。
- **C 样例数据**：`TZHJ.SampleData` 生成。

## 构建与运行

**Windows（开发/运行客户端）：**

```powershell
dotnet build TZHJ.sln
dotnet run --project src/TZHJ.App        # 启动客户端（默认 UseMock=true）
```

登录：工号任意非空（如 `10086`）、密码任意（填 `fail` 可演示登录失败）。本地根目录默认
`D:\TZHJ_Data`（见 `src/TZHJ.App/appsettings.json`）。先跑一次样例生成器把数据铺到该目录即可看到批次。

**Linux / macOS / CI（仅验证编译，不能运行 WPF）：**

```bash
dotnet build TZHJ.sln -p:EnableWindowsTargeting=true
```

## 样例数据生成器

铺出 `{核价|挑图}/<工号>/{待处理|已处理|异常待跟进}/<时间窗>/` 全套（清单表格.xlsx + 占位 PDF/STEP + manifest）：

```bash
dotnet run --project src/TZHJ.SampleData -- --root "D:\TZHJ_Data" --employee 10086
```

每流程生成 4 个批次：待处理·未处理、待处理·处理中、2 个已处理（含异常行入池）。

## 真接口到位后怎么切换

1. 在 `TZHJ.Infrastructure` 新增 `HttpAuthGateway / HttpDataGateway / HttpSubmitGateway / HttpConfigGateway`
   （实现同一组 `Core/Contracts` 接口，内部 HttpClient 调后端无状态网关）。
2. 加一个 `AddTzhjHttpInfrastructure(...)` 注册扩展。
3. `App.xaml.cs` 里把 `AddTzhjMockInfrastructure` 换成它（或用 `appsettings.json` 的 `UseMock` 开关）。

UI、ViewModel、本地存储**均不改动**。

## 字段配置化

表单列、xlsx 列、网格列都由 `FieldDefinition` 列表驱动（`Schemas/FieldSchemas.cs`）；
登录后 `IConfigGateway` 下发的字段集会覆盖默认 schema（`DefaultFieldProvider.Apply`）。**加字段不改代码。**

## 自动更新

按方案选型走 **ClickOnce**。发布配置在 `src/TZHJ.App/Properties/PublishProfiles/ClickOnceProfile.pubxml`
（普通 `dotnet build` 不读它，只在显式发布时生效，故不影响日常编译 / CI）。

**在 Windows 上发布**（需 .NET 8 SDK）：

```powershell
dotnet publish src/TZHJ.App/TZHJ.App.csproj -p:PublishProfile=ClickOnceProfile
```

或在 Visual Studio 右键 `TZHJ.App` → 发布 → 选 `ClickOnceProfile`。

上线前要改 `.pubxml` 里两处占位：`PublishUrl` / `InstallUrl` 指向真实发布目标（文件共享
`\\server\share\TZHJ` 或 https 站点）。更新策略已设为 **启动前同步检查**（`UpdateMode=Foreground`）——
管理员发一次新版，所有客户端下次启动即拉到最新（呼应"改一处、不逐台重打包"）。**每次发布请递增
`ApplicationVersion` 末位**，客户端才能检测到新版本。框架依赖发布默认需目标机有 .NET 8 桌面运行时
（Bootstrapper 可代装）；想免预装可在 `.pubxml` 把 `SelfContained` 改 `true`（体积更大）。

## 与外部系统待确认项（来自 `docs/方案设计.md` §9，影响最终回传/取数实现）

- 取数携带标识（是否工号）及源系统据此返回本人数据的方式；两流程 EBS 取数如何区分。
- 挑图回传 EBS 的关联键（是否即 EBS-ID）与回传字段映射；核价回传 SRM 字段映射。
- PLM "是否存在变更" 字段的取值/含义与接口形态（是否与图纸同接口返回）、图纸版本策略。
- DHR 认证协议与可提供字段；回传幂等 / 部分失败处理。
