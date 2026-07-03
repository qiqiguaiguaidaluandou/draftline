using Microsoft.EntityFrameworkCore;
using Draftline.Core.Enums;
using Draftline.Gateway.Stores;

namespace Draftline.Gateway.Auth;

/// <summary>一条数据可见范围：某流程的某组（GroupName="*" = 全部组）。</summary>
public readonly record struct Grant(FlowType Flow, string GroupName);

/// <summary>
/// 把用户的角色展开成有效数据范围（各角色 (流程,组) 的并集），并提供访问判定。
/// 全系统的"能看哪些数据"唯一入口——数据端点不再各自内联权限查询。
/// </summary>
public interface IPermissionService
{
    /// <summary>该用户的全部有效 (流程,组) 范围（去重）。</summary>
    Task<List<Grant>> GetGrantsAsync(string employeeId, CancellationToken ct = default);

    /// <summary>该用户在某流程下被授权的流程集合（去重）。</summary>
    Task<List<FlowType>> GetFlowsAsync(string employeeId, CancellationToken ct = default);

    /// <summary>该用户是否可访问 (flow, groupName)（命中具体组或该流程通配 "*"）。</summary>
    Task<bool> HasAccessAsync(string employeeId, FlowType flow, string groupName, CancellationToken ct = default);
}

public sealed class PermissionService : IPermissionService
{
    private readonly DraftlineDbContext _db;

    public PermissionService(DraftlineDbContext db) => _db = db;

    /// <summary>展开某用户角色含的全部 (流程,组)。复用于多个查询。</summary>
    private IQueryable<RolePermission> RolePermissionsOf(string employeeId) =>
        _db.UserRoles
            .Where(ur => ur.EmployeeId == employeeId)
            .SelectMany(ur => ur.Role!.Permissions);

    public async Task<List<Grant>> GetGrantsAsync(string employeeId, CancellationToken ct = default) =>
        await RolePermissionsOf(employeeId)
            .Select(rp => new Grant(rp.Flow, rp.GroupName))
            .Distinct()
            .ToListAsync(ct);

    public async Task<List<FlowType>> GetFlowsAsync(string employeeId, CancellationToken ct = default) =>
        await RolePermissionsOf(employeeId)
            .Select(rp => rp.Flow)
            .Distinct()
            .ToListAsync(ct);

    public async Task<bool> HasAccessAsync(string employeeId, FlowType flow, string groupName, CancellationToken ct = default) =>
        await RolePermissionsOf(employeeId)
            .AnyAsync(rp => rp.Flow == flow && (rp.GroupName == "*" || rp.GroupName == groupName), ct);
}
