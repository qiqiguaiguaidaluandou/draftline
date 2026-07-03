using Draftline.Core.Enums;

namespace Draftline.Core.Contracts.Http;

// ========== 共享 wire DTO ==========
// 客户端（Draftline.Infrastructure 的 Http*Gateway）与后端（Draftline.Gateway）共用同一份定义，天然对齐。
// 能直接复用的现有契约（AuthResult / ClientConfig / SubmitRequest / SubmitResult / FetchRequest）就复用；
// 只为"两阶段取数"（行+图纸清单，图纸字节走单独流式端点）新增下面这些。

/// <summary>登录请求体。对应 IAuthGateway.LoginAsync(employeeId, password)。</summary>
public sealed class LoginRequest
{
    public required string EmployeeId { get; init; }
    public required string Password { get; init; }
}

/// <summary>本人改密请求体（POST /api/auth/change-password，需令牌；工号以令牌为准）。</summary>
public sealed class ChangePasswordRequest
{
    public required string OldPassword { get; init; }
    public required string NewPassword { get; init; }
}

/// <summary>通用结果（改密 / 管理操作）：成功标志 + 提示文案。</summary>
public sealed class ApiResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }

    public static ApiResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static ApiResult Fail(string message) => new() { Success = false, Message = message };
}

// ========== 管理端（/api/admin/*，仅管理员）==========

/// <summary>管理员新建用户。初始密码下发后用户首登强制改密。</summary>
public sealed class CreateUserRequest
{
    public required string EmployeeId { get; init; }
    public required string DisplayName { get; init; }
    public string? Department { get; init; }
    public string? Position { get; init; }
    public required string InitialPassword { get; init; }
    public bool IsAdmin { get; init; }
}

/// <summary>管理员重置某用户密码（重置后该用户首登强制改密、解锁）。</summary>
public sealed class ResetPasswordRequest
{
    public required string NewPassword { get; init; }
}

/// <summary>管理员启用/停用某用户。</summary>
public sealed class SetActiveRequest
{
    public bool IsActive { get; init; }
}

/// <summary>一条数据范围：流程+组。GroupName="*" 代表该流程全部组。</summary>
public sealed class PermissionDto
{
    public required FlowType Flow { get; init; }
    public required string GroupName { get; init; }
}

// ----- 角色（数据范围捆绑）-----

/// <summary>角色引用（用户列表里显示其挂了哪些角色）。</summary>
public sealed class RoleRef
{
    public int Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>角色详情（含其数据范围）。</summary>
public sealed class RoleSummary
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<PermissionDto> Permissions { get; init; } = new();
    public int UserCount { get; init; }
}

/// <summary>新建/更新角色（覆盖式设置其数据范围）。</summary>
public sealed class SaveRoleRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<PermissionDto> Permissions { get; init; } = new();
}

/// <summary>给某用户整体替换所挂角色（覆盖式）。</summary>
public sealed class SetUserRolesRequest
{
    public List<int> RoleIds { get; init; } = new();
}

/// <summary>用户列表项（不含任何密码信息）。</summary>
public sealed class UserSummary
{
    public required string EmployeeId { get; init; }
    public required string DisplayName { get; init; }
    public string? Department { get; init; }
    public string? Position { get; init; }
    public bool IsActive { get; init; }
    public bool IsAdmin { get; init; }
    public bool MustChangePassword { get; init; }
    public bool IsLocked { get; init; }
    /// <summary>所挂角色。</summary>
    public List<RoleRef> Roles { get; init; } = new();
    /// <summary>角色展开后的有效数据范围（并集去重）。</summary>
    public List<PermissionDto> EffectivePermissions { get; init; } = new();
}

// ----- 管理端：操作日志 / 可选组 -----

/// <summary>管理端日志一条（全量 ActivityLogs 投影）。</summary>
public sealed class AdminLogEntry
{
    public long Id { get; init; }
    /// <summary>UTC 时间。</summary>
    public DateTime Timestamp { get; init; }
    public required string EmployeeId { get; init; }
    public required string Action { get; init; }
    public required string Status { get; init; }
    public FlowType? Flow { get; init; }
    public string? GroupName { get; init; }
    public string? BatchId { get; init; }
    public int ImpactCount { get; init; }
    public string? Payload { get; init; }
    public string? ClientIp { get; init; }
}

/// <summary>管理端日志查询响应（分页）。</summary>
public sealed class AdminLogListResponse
{
    public int Total { get; init; }
    public List<AdminLogEntry> Items { get; init; } = new();
}

/// <summary>可选数据范围（来自现有批次的 流程+组），配角色时选用，避免手打错组名。</summary>
public sealed class GroupOption
{
    public required FlowType Flow { get; init; }
    public required string GroupName { get; init; }
}

/// <summary>
/// 取数响应（第一阶段）：行数据 + 每行图纸的**元数据**（不含字节）。
/// 客户端拿到后对每张图纸调 GET /api/drawings 流式下载，再拼成 Draftline.Core 的 FetchResult。
/// </summary>
public sealed class FetchResponse
{
    public bool Success { get; init; }
    public required FlowType Flow { get; init; }
    public required string EmployeeId { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }
    /// <summary>归属组名（多组拆分时使用）。</summary>
    public string? GroupName { get; set; }
    public List<FetchRowDto> Rows { get; init; } = new();
    public string? Message { get; init; }
}

/// <summary>取数响应的一行：行标识 + 只读字段值 + 该行图纸元数据（无字节）。</summary>
public sealed class FetchRowDto
{
    public required string RowKey { get; init; }
    public Dictionary<string, string?> Values { get; init; } = new();
    public List<DrawingMeta> Drawings { get; init; } = new();
}

/// <summary>图纸元数据（无字节）。DrawingId 直接用"料号前缀文件名"，天然唯一、含料号。</summary>
public sealed class DrawingMeta
{
    public required string DrawingId { get; init; }
    public required string FileName { get; init; }
    public required string MaterialCode { get; init; }
    public long Size { get; init; }
}

/// <summary>登录补拉用：查某窗口是否已成功回传过（本地无 + 后端查不到才补拉，避免重复回传）。</summary>
public sealed class AuditExistsResponse
{
    public bool Exists { get; init; }
    public string? AuditId { get; init; }
}

/// <summary>
/// 用户操作日志一条（集中上报）。管理员在服务器侧查全部，操作员在 App 内只查自己（按令牌工号过滤）。
/// 记录时机：回传/补回传成功之后。本期只记这两个动作，后续可扩展（加 Operation 取值即可）。
/// </summary>
public sealed class OperationLogEntry
{
    /// <summary>操作按钮（如"回传到SRM""重新回传到EBS"）。</summary>
    public required string Operation { get; init; }

    /// <summary>操作电脑 IP（客户端本机局域网 IPv4）。</summary>
    public string? ClientIp { get; init; }

    /// <summary>表单名称（= 批次文件夹名）。</summary>
    public required string FormName { get; init; }

    /// <summary>操作时间（客户端本机时间）。</summary>
    public required DateTime OperatedAt { get; init; }

    /// <summary>所属流程（核价/挑图），便于服务器侧归类。</summary>
    public FlowType Flow { get; init; }

    /// <summary>
    /// 工号。客户端上报时可不填——后端以令牌为准盖章写入（防伪造）；
    /// 查询返回时为该条所属工号。
    /// </summary>
    public string? EmployeeId { get; init; }

    /// <summary>操作结果（"成功"/"失败"）。仅 /oplog/mine 查询返回时填充；上报时留空。</summary>
    public string? Result { get; init; }

    /// <summary>流程中文名，供 App 直接绑定显示（不入库、不上报）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string FlowLabel => Flow == FlowType.Pricing ? "核价" : "挑图";
}

/// <summary>本人操作日志查询响应（GET /api/oplog/mine）。</summary>
public sealed class OperationLogListResponse
{
    public List<OperationLogEntry> Items { get; init; } = new();
}
