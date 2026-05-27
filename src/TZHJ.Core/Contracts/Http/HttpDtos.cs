using TZHJ.Core.Enums;

namespace TZHJ.Core.Contracts.Http;

// ========== 共享 wire DTO ==========
// 客户端（TZHJ.Infrastructure 的 Http*Gateway）与后端（TZHJ.Gateway）共用同一份定义，天然对齐。
// 能直接复用的现有契约（AuthResult / ClientConfig / SubmitRequest / SubmitResult / FetchRequest）就复用；
// 只为"两阶段取数"（行+图纸清单，图纸字节走单独流式端点）新增下面这些。

/// <summary>登录请求体。对应 IAuthGateway.LoginAsync(employeeId, password)。</summary>
public sealed class LoginRequest
{
    public required string EmployeeId { get; init; }
    public required string Password { get; init; }
}

/// <summary>
/// 取数响应（第一阶段）：行数据 + 每行图纸的**元数据**（不含字节）。
/// 客户端拿到后对每张图纸调 GET /api/drawings 流式下载，再拼成 TZHJ.Core 的 FetchResult。
/// </summary>
public sealed class FetchResponse
{
    public bool Success { get; init; }
    public required FlowType Flow { get; init; }
    public required string EmployeeId { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }
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
