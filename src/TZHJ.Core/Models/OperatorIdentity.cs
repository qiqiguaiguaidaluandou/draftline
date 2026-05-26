using TZHJ.Core.Enums;

namespace TZHJ.Core.Models;

/// <summary>
/// 登录后的操作员身份与权限。认证（DHR/SSO，先占位）+ 授权（流程权限 + RBAC 功能权限）。
/// </summary>
public sealed class OperatorIdentity
{
    public required string EmployeeId { get; init; }

    public required string DisplayName { get; init; }

    public string? Department { get; init; }

    public string? Position { get; init; }

    /// <summary>流程权限：能进入哪些功能区。</summary>
    public IReadOnlyList<FlowType> AllowedFlows { get; init; } = Array.Empty<FlowType>();

    /// <summary>RBAC 功能权限：能否核价/填写（作业）。</summary>
    public bool CanOperate { get; init; } = true;

    /// <summary>RBAC 功能权限：能否整批提交（回传）。</summary>
    public bool CanSubmit { get; init; } = true;

    public bool CanAccess(FlowType flow) => AllowedFlows.Contains(flow);
}
