using Draftline.Core.Enums;

namespace Draftline.Gateway.Stores;

/// <summary>
/// 活动日志统一写入入口。收敛各端点重复手抄的 <c>Timestamp = DateTime.UtcNow</c> + <c>ActivityLogs.Add(...)</c>，
/// 只暴露业务字段。**不** SaveChanges（由调用方与其它变更同事务提交）；返回新建的 <see cref="ActivityLog"/>，
/// 供个别调用方补结构化列（如 Submit 的 <c>WindowStart/End/AuditId</c>）。
/// </summary>
public static class ActivityLogExtensions
{
    public static ActivityLog LogActivity(
        this DraftlineDbContext db,
        string action,
        string employeeId,
        string? clientIp = null,
        FlowType? flow = null,
        string? groupName = null,
        string? batchId = null,
        int impactCount = 0,
        string status = "Success",
        string? payload = null)
    {
        var log = new ActivityLog
        {
            Action = action,
            EmployeeId = employeeId,
            ClientIp = clientIp,
            Flow = flow,
            GroupName = groupName,
            BatchId = batchId,
            ImpactCount = impactCount,
            Status = status,
            Payload = payload,
            Timestamp = DateTime.UtcNow,
        };
        db.ActivityLogs.Add(log);
        return log;
    }
}
