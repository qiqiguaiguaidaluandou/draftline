# 开发文档 ① — 后端骨架 + 真 HTTP 链路

> 对应 `无接口期开发清单.md` 的 ①、`路线图.md` 的轨道 B0/C。目标：在真实 EBS/PLM/SRM/DHR
> 接口到位前，把"未来 Mock→Http 切换"这一步提前演练掉，使届时改动只剩后端防腐层内部一层。
> 需求权威见 `方案设计.md`。最后更新：2026-05-27。

---

## 1. 目标与范围

**交付**：一个可本机运行的 ASP.NET Core 无状态网关骨架 + 客户端真 HTTP 网关实现，端到端跑通
"登录 → 取数（行+图纸落本地） → 作业 → 整批回传 → 移入已处理 → 登录补拉查审计"全流程，
**后端那头数据仍是 Mock（假防腐层）**。

**显式不做（留给路线图 A/B1/B2）**：真正调 EBS/PLM/SRM/DHR；真实凭证管理；生产级鉴权。
这些都收敛在后端"防腐层接缝"后面，本期用 Fake 实现顶替。

**不变量**：`Draftline.Core` 的网关契约（`IAuthGateway`/`IDataGateway`/`ISubmitGateway`/`IConfigGateway`）
与 UI/`ILocalBatchStore` **一律不动**。本期只新增后端工程 + 客户端 `Http*Gateway` 实现 + DI 切换。

---

## 2. 总体设计：两层"假实现"接缝

```
┌─ 客户端 (Draftline.App + Draftline.Infrastructure) ──────────────┐
│  UI / ViewModel  (不动)                                  │
│  ILocalBatchStore = LocalBatchStore  (不动)              │
│  IAuthGateway/IDataGateway/ISubmitGateway/IConfigGateway │
│    ├─ AddDraftlineMockInfrastructure  → Mock*Gateway (保留=离线模式) │
│    └─ AddDraftlineHttpInfrastructure  → Http*Gateway  ★本期新增      │
└───────────────────────────────┬────────────────────────┘
                                 │  HTTP (JSON + 图纸流)
┌─ 后端 (Draftline.Gateway, ASP.NET Core 无状态) ─┴────────────┐
│  端点: /auth /config /fetch /drawings /submit /audit     │
│  防腐层接缝:                                              │
│    IEbsPlmSource / ISrmSink / IEbsSink                   │
│      ├─ FakeDataSource  (移植现 Mock 生成逻辑) ★本期      │
│      └─ Ebs/Plm/SrmHttpClient (真接口到位后) ← 路线图 B1/B2│
│  IConfigStore / IAuditStore  (骨架: 内存/JSON → 后置 PG)  │
└──────────────────────────────────────────────────────────┘
```

要点：客户端那层接缝（Mock→Http）是本期的"演练对象"；后端内部那层接缝（Fake→Real）是
**未来真接口唯一要改的地方**。本期把第二层接缝搭出来、用 Fake 顶上，真接口来了只换 Fake 那一块。

---

## 3. 解决方案结构变化

```
Draftline.sln
├─ src/Draftline.Core            (不动 + 新增 Contracts/Http 共享 wire DTO)
├─ src/Draftline.Infrastructure  (新增 Gateways/Http/ 四个 Http*Gateway + HttpOptions)
├─ src/Draftline.App             (改 App.xaml.cs 的 DI 分支 + appsettings 加 Http 节)
├─ src/Draftline.SampleData      (不动)
└─ src/Draftline.Gateway         ★新增：ASP.NET Core 无状态网关
   ├─ Program.cs            (Minimal API 端点 + DI)
   ├─ Endpoints/            (auth/config/fetch/drawings/submit/audit)
   ├─ AntiCorruption/       (IEbsPlmSource/ISrmSink/IEbsSink + FakeDataSource)
   ├─ Stores/               (IConfigStore/IAuditStore + 内存实现)
   └─ Auth/                 (Bearer 占位校验)
```

**共享 wire DTO 放 `Draftline.Core/Contracts/Http/`**：客户端与后端都引用 `Draftline.Core`，同一份定义，
天然对齐。能直接复用的现有 DTO（`AuthResult`/`ClientConfig`/`SubmitRequest`/`SubmitResult`/`FetchRequest`）
就复用；只为"两阶段取数"新增 `FetchResponse` / `FetchRowDto` / `DrawingMeta`。

---

## 4. HTTP 接口契约

约定：业务失败（如认证不通过、回传被拒）走 `200 + body.success=false`（与现有 `AuthResult.Fail`/
`SubmitResult{Success=false}` 一致）；协议层错误才用 4xx/5xx。除登录与健康检查外，所有请求带
`Authorization: Bearer {token}`。JSON 用驼峰。

| 方法 | 路径 | 入 | 出 | 说明 |
|---|---|---|---|---|
| GET | `/healthz` | — | 200 | 存活探测 |
| POST | `/api/auth/login` | `{employeeId, password}` | `AuthResult` | 占位认证→将来 DHR |
| GET | `/api/config` | `?employeeId=` | `ClientConfig` | 时间窗/字段集/本地根/保留天数 |
| POST | `/api/fetch` | `FetchRequest` | `FetchResponse`（行+图纸清单，**无字节**） | 防腐层取 EBS+PLM |
| GET | `/api/drawings/{batchKey}/{drawingId}` | — | `200 application/octet-stream` | 流式下载单张图纸 |
| POST | `/api/submit` | `SubmitRequest` | `SubmitResult`（含 `auditId`） | 整批回传 + 记审计 |
| GET | `/api/audit/exists` | `?flow=&employeeId=&windowStart=&windowEnd=` | `{exists, auditId?}` | 登录补拉判"是否已回传过" |

### 4.1 取数（两阶段，已定）

`POST /api/fetch` 返回行 + 每行的图纸**元数据**（不含字节），保留行↔图纸归属：

```jsonc
// FetchResponse
{
  "success": true,
  "flow": "Pricing",                 // 或 "DrawingSelection"
  "employeeId": "10086",
  "windowStart": "2026-05-26T15:31:00",
  "windowEnd":   "2026-05-27T09:30:00",
  "rows": [
    {
      "rowKey": "M-10231",
      "values": { "materialCode": "M-10231", "model": "GB-3344",
                  "name": "支架A / Q235 3mm", "demandQty": "120",
                  "hasChange": "无变更", "targetPrice": null },
      "drawings": [                  // ★只有元数据
        { "drawingId": "M-10231__支架A.pdf",  "fileName": "M-10231__支架A.pdf",
          "materialCode": "M-10231", "size": 1234 },
        { "drawingId": "M-10231__支架A.step", "fileName": "M-10231__支架A.step",
          "materialCode": "M-10231", "size": 567 }
      ]
    }
  ],
  "message": null
}
```

`drawingId` 直接用"料号前缀文件名"（已天然唯一、含料号），免再造 id。无图纸的行 `drawings` 为空数组
（对应现 `DrawingMissingRate`，UI 标"缺失"）。

`GET /api/drawings/{batchKey}/{drawingId}` 返回该文件字节流（`Content-Disposition` 带文件名）；
找不到 → `404`（客户端按"图纸缺失"处理，与空清单一致）。`batchKey` = 现 `SubmitRequest.BatchKey`
同构（流程+窗口起止），后端据此定位/重建该批文件。

> 后端 Fake 模式下取数是**确定性的**（种子=工号+流程+窗口），故 `/drawings` 可按同一种子重新生成同一字节；
> 真实模式由防腐层向 PLM 取（必要时后端短期缓存该批，避免二次取数版本漂移）。

### 4.2 客户端如何还原 `FetchResult`

`HttpDataGateway.FetchBatchAsync` 内部：① `POST /fetch` 拿 `FetchResponse`；② 对每行每张图纸
`GET /drawings/...` 下载字节，填成 `FetchedDrawing{FileName, MaterialCode, Content}`；③ 拼成与今天 Mock
**完全同形**的 `FetchResult` 返回。`LocalBatchStore.WriteFetchedBatchAsync` 及以上全不变。

---

## 5. 关键设计决策

| # | 决策 | 取值 | 说明 |
|---|---|---|---|
| D1 | 图纸传输 | **两阶段流式** | 已定（见 §4.1）。契约不变，适合厚客户端落大文件 |
| D2 | 身份绑定 | **以 token 为准** | 后端从 token 解析工号，请求体 `employeeId` 仅作校验/忽略；落实"数据范围由取数带工号在源头限定" |
| D3 | 网关地址引导 | **appsettings 引导 URL** | 先有 `Http:BaseUrl` 才能调 `/config`；`ClientConfig.GatewayBaseUrl` 留作下发覆盖位（骨架先用单一 URL） |
| D4 | 配置/审计存储 | **骨架内存/JSON，DB 后置** | `IConfigStore` 先从 `Draftline.Core` 默认 schema 种子；`IAuditStore` 先内存。**审计表结构本期定下**（追溯刚需） |
| D5 | 后端框架风格 | **Minimal API** | 轻、够用；端点少 |
| D6 | 鉴权占位强度 | **校验 token 非空 + 绑定身份** | 比"完全放行"更接近真实，省得以后改流程；真实校验（DHR）留防腐层 |

> D2–D6 是我建议的默认值，落在文档里方便照做；其中任何一条你想改，一句话即可，我同步改文档/实现。

---

## 6. 后端内部结构（防腐层接缝 = 未来唯一改动点）

```csharp
// AntiCorruption/  —— 真接口到位后，只换这层的实现（路线图 B1/B2）
public interface IEbsPlmSource {
    Task<IReadOnlyList<SourceRow>> FetchRowsAsync(FlowType flow, string empId, DateTime ws, DateTime we, CancellationToken ct);
    Task<byte[]?> OpenDrawingAsync(string batchKey, string drawingId, CancellationToken ct); // 404→null
}
public interface ISubmitSink {   // 核价→SRM / 挑图→EBS，按 flow 分发
    Task<IReadOnlyList<SubmitRowResult>> SubmitAsync(FlowType flow, IReadOnlyList<SubmitRow> rows, CancellationToken ct);
}

// FakeDataSource : IEbsPlmSource, ISubmitSink  —— 本期实现，移植 MockDataGateway/MockSubmitGateway 生成逻辑
//   - 行/字段/图纸字节：复用现 MockDataGateway 的确定性生成（种子相关）
//   - 回传：复用现 MockSubmitGateway（可配失败率，调失败态）
```

`IConfigStore.GetAsync(empId) → ClientConfig`：骨架实现从 `Draftline.Core` 的 `CollectionSchedules.*` +
`FieldSchemas.*` 种子（即把今天 `MockConfigGateway` 的默认值挪到后端）。
`IAuditStore`：`RecordAsync(submit)` 记一条；`ExistsAsync(flow, empId, ws, we)` 供补拉查。**审计字段**（本期定）：
审计号、流程、工号、批次键、窗口起止、目标系统(SRM/EBS)、行数、结果、时间。

---

## 7. 客户端改造

**新增 `src/Draftline.Infrastructure/Gateways/Http/`**：`HttpAuthGateway`/`HttpConfigGateway`/
`HttpDataGateway`（含 §4.2 下载拼装）/`HttpSubmitGateway`，各自实现对应 `Draftline.Core` 契约。

**新增 `HttpOptions`**（绑定 appsettings `Http` 节）：`BaseUrl`、`TimeoutSeconds`、（可选）重试次数。

**新增 `AddDraftlineHttpInfrastructure(this IServiceCollection, HttpOptions)`**（与 `AddDraftlineMockInfrastructure` 并存）：
- 注册 typed `HttpClient`（BaseAddress=BaseUrl，超时）；
- 注册一个 `AuthTokenHandler : DelegatingHandler`，从 `ISession` 取登录 token 加 `Bearer`（登录响应里的 `Token` 存进 `ISession`）；
- 注册四个 `Http*Gateway`；
- `ILocalBatchStore = LocalBatchStore`（不变）、`IFieldProvider = DefaultFieldProvider`（登录取 `ClientConfig` 后 `.Apply` 覆盖，机制不变）。

**`App.xaml.cs`**：把现在 `else` 分支的 `throw`（第 42 行）换成读 `Http` 节 + `services.AddDraftlineHttpInfrastructure(http)`；
`UseMock` 开关保留（`true`=离线 Mock，`false`=走后端）。

**`appsettings.json`** 增：
```jsonc
"UseMock": false,
"Http": { "BaseUrl": "http://localhost:8080", "TimeoutSeconds": 60 }
```

---

## 8. 数据库（骨架可后置，结构先定）

骨架阶段用内存/JSON 即可跑通；下面三张表是上线前要落 PostgreSQL 的（后端角色仅此三类，见方案）：

- `config`：键→值（或整份 `ClientConfig` JSON），驱动"改配置即生效、不重发客户端"。
- `permission`：工号→流程权限/功能权限（核价/提交），驱动授权。
- `audit_log`：见 §6 审计字段。**追溯唯一抓手**（本地删了就靠它），补拉也查它——本期即便用内存，结构也按此定。

---

## 9. 开发步骤（建议顺序）与验收

1. 加 `Draftline.Gateway` 工程入解；`Draftline.Core/Contracts/Http/` 加 `FetchResponse`/`FetchRowDto`/`DrawingMeta`。
2. 后端防腐层接缝 + `FakeDataSource`（移植 Mock 生成）；`IConfigStore`/`IAuditStore` 内存实现。
3. 后端 Minimal API 端点（§4）+ Bearer 占位校验 + `/healthz`。
4. 客户端 `Http*Gateway`（含 `HttpDataGateway` 两阶段下载拼装）+ `AuthTokenHandler`。
5. `AddDraftlineHttpInfrastructure` + `App.xaml.cs` 切换 + `appsettings` Http 节。
6. 端到端联调。
7. （可后置）落 PostgreSQL：config / audit。

**验收标准（端到端，UseMock=false 指向本机后端）**：
- [x] `/healthz` 200；客户端 HTTP 登录成功、拿到 token。（契约级已验：curl 走通 401/失败/成功三态）
- [x] 取数：行数据 + 图纸**逐张下载到本地批次目录**，结果与今天 Mock 模式**目录树/xlsx 一致**。（契约级已验：两阶段 fetch + 图纸两次独立请求**字节完全一致**；客户端 `HttpDataGateway` 拼装成同形 `FetchResult`，落本地由不变的 `LocalBatchStore` 负责。**真机目录树/xlsx 比对待 Windows 跑**）
- [x] 作业 → 整批回传成功，返回 `auditId`，批次目录从「待处理」移入「已处理」。（契约级已验：`/submit` 成功返回 auditId、核价→SRM/挑图→EBS 路由正确；目录迁移由不变的客户端负责，**真机待 Windows 跑**）
- [~] 补拉：删「已处理」批次（窗口内）→ 登录补拉查 `/audit/exists` 命中 → **不重拉**；删「待处理」（无审计）→ **重拉**。（后端 `/audit/exists` 已实现并验通：回传前 false→回传后 true 且同 auditId、不串窗口。**但客户端补拉目前只查本地**（`BatchListViewModel`），消费后端审计属清单②"登录补拉+查审计日志"，本项①不含）
- [x] `UseMock=true` 离线模式仍照常工作（未回退）。（Mock 路径未改，整解编译通过）
- [x] 整解 `dotnet build`（App 在 Linux 加 `-p:EnableWindowsTargeting=true`）0 错。（已验：0 警告 0 错误）

> **实现说明（与上文契约的两处差异，均以已落地代码为准）**：
> 1. **令牌持有位置**：§7 原写"从 `ISession` 取 token"。但 `ISession` 在 `Draftline.App`，而令牌处理器 `AuthTokenHandler`
>    在 `Draftline.Infrastructure`，不能反向依赖 App；且令牌须在"登录响应到手"与"取配置(受保护)"之间就绪、早于
>    `ISession.SignIn`。故改为 Infrastructure 内的单例 `IAuthTokenStore`：`HttpAuthGateway` 登录成功写入，
>    `AuthTokenHandler` 读出加 `Bearer`。UI/`ISession` **仍未改**。
> 2. **图纸/审计端点用 query 形参**：实际为 `GET /api/drawings?flow=&windowStart=&windowEnd=&drawingId=`、
>    `GET /api/audit/exists?flow=&windowStart=&windowEnd=`（§4 表里的 `/{batchKey}/{drawingId}` 路径形未采用）。
>    无状态确定性重生靠"流程+窗口起止"重建批次，窗口起止用 `FetchResponse` 回显值经 `:O` 往返，与 `/fetch` 同种子。

---

## 10. 待你拍板的剩余决策点

§5 的 D2–D6 已给建议默认值，照做即可；若要改，告诉我哪条。其余如真实凭证、生产鉴权、PostgreSQL
正式建表，按路线图留到 B 阶段，本期不展开。
