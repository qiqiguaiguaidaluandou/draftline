using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.Core.Contracts;

// ========== 认证 ==========

/// <summary>登录结果（IAuthGateway）。</summary>
public sealed class AuthResult
{
    public bool Success { get; init; }
    public OperatorIdentity? Operator { get; init; }
    /// <summary>会话令牌（后续调用带上；为带签名/过期的 JWT）。</summary>
    public string? Token { get; init; }
    public string? Message { get; init; }
    /// <summary>是否必须先改密（管理员新建/重置后下发的初始密码，登录后强制修改）。</summary>
    public bool MustChangePassword { get; init; }

    public static AuthResult Fail(string message) => new() { Success = false, Message = message };
}

// ========== 取数（EBS 需求 + PLM 图纸/是否变更） ==========

/// <summary>取数请求：某工号、某流程、某时间窗。</summary>
public sealed class FetchRequest
{
    public required string EmployeeId { get; init; }
    public required FlowType Flow { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }

    /// <summary>目标产品线组（可选）。如果源头已分批，由请求方指定。</summary>
    public string? GroupName { get; set; }
}

/// <summary>取数返回的一行：行标识 + 只读字段值（EBS+PLM）+ 该行图纸（含字节内容）。</summary>
public sealed class FetchedRow
{
    public required string RowKey { get; init; }
    /// <summary>字段值，key = FieldDefinition.Key（只含取数来的只读字段；待填列为空待操作员填）。</summary>
    public Dictionary<string, string?> Values { get; init; } = new();
    public List<FetchedDrawing> Drawings { get; init; } = new();
}

/// <summary>取数返回的一张图纸（PLM 附件）。Content 为文件字节，由客户端落本地。</summary>
public sealed class FetchedDrawing
{
    public required string FileName { get; init; }
    public required string MaterialCode { get; init; }
    public required byte[] Content { get; init; }
}

/// <summary>取数结果：一个批次的全部内容。</summary>
public sealed class FetchResult
{
    public bool Success { get; init; }
    public required FlowType Flow { get; init; }
    public required string EmployeeId { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }
    /// <summary>归属组名。</summary>
    public string? GroupName { get; set; }
    public List<FetchedRow> Rows { get; init; } = new();
    public string? Message { get; init; }
}

// ========== 回传（核价→SRM / 挑图→EBS） ==========

/// <summary>回传的一行（仅正常行）：关联键 + 随行字段值。</summary>
public sealed class SubmitRow
{
    public required string RowKey { get; init; }
    public Dictionary<string, string?> Values { get; init; } = new();
}

/// <summary>整批回传请求。挂起异常行不在内。</summary>
public sealed class SubmitRequest
{
    public required string EmployeeId { get; init; }
    public required FlowType Flow { get; init; }
    /// <summary>归属产品线组。</summary>
    public required string GroupName { get; init; }
    /// <summary>批次键（流程+窗口起止），幂等/审计用。</summary>
    public required string BatchKey { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }
    public List<SubmitRow> Rows { get; init; } = new();
}

public sealed class SubmitRowResult
{
    public required string RowKey { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
}

/// <summary>回传结果。后端执行回传并记审计日志，返回审计号。</summary>
public sealed class SubmitResult
{
    public bool Success { get; init; }
    /// <summary>后端回传审计日志 ID（本地即状态模式下唯一可追溯抓手）。</summary>
    public string? AuditId { get; init; }
    public List<SubmitRowResult> RowResults { get; init; } = new();
    public string? Message { get; init; }
}
