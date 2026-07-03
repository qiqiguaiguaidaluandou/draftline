using Draftline.Core.Enums;

namespace Draftline.Gateway.Stores;

/// <summary>审计存储 PostgreSQL 实现。</summary>
public sealed class PgAuditStore : IAuditStore
{
    private readonly DraftlineDbContext _db;

    public PgAuditStore(DraftlineDbContext db) => _db = db;

    public string Record(FlowType flow, string employeeId, string batchKey, DateTime windowStart, DateTime windowEnd, string target, int rowCount)
    {
        var auditId = $"AUDIT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..36];
        var log = new ActivityLog
        {
            Timestamp = DateTime.UtcNow,
            EmployeeId = employeeId,
            Action = "Submit",
            Flow = flow,
            BatchId = batchKey,
            ImpactCount = rowCount,
            Status = "Success",
            Payload = $"Target: {target}, AuditId: {auditId}, Window: {windowStart:O} - {windowEnd:O}",
        };

        _db.ActivityLogs.Add(log);
        _db.SaveChanges();
        return auditId;
    }

    public (bool Exists, string? AuditId) Find(FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd)
    {
        // 补拉判据：精确匹配流程、工号，并在 Payload 中寻找窗口起止（新方案下窗口存入了 Payload JSON/Text）
        // 演示环境暂时使用简单的 Contain 检查
        var ws = windowStart.ToUniversalTime().ToString("O");

        var hit = _db.ActivityLogs
            .Where(r => r.Flow == flow && r.EmployeeId == employeeId && r.Action == "Submit")
            .OrderByDescending(r => r.Timestamp)
            .AsEnumerable()
            .FirstOrDefault(r => r.Payload != null && r.Payload.Contains(ws));

        if (hit == null) return (false, null);

        // 从 Payload 提取 AuditId（简单逻辑）
        var auditId = hit.Payload?.Split("AuditId: ")[1].Split(",")[0];
        return (true, auditId);
    }
}
