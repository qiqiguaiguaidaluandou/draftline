using Draftline.Core.Contracts.Http;

namespace Draftline.Gateway.Stores;

/// <summary>用户操作日志 PostgreSQL 持久化实现。</summary>
public sealed class PgOperationLogStore : IOperationLogStore
{
    private readonly DraftlineDbContext _db;

    public PgOperationLogStore(DraftlineDbContext db) => _db = db;

    public void Append(OperationLogEntry entry)
    {
        var log = new ActivityLog
        {
            Timestamp = entry.OperatedAt.ToUniversalTime(),
            EmployeeId = entry.EmployeeId,
            Action = "Behavior",
            Flow = entry.Flow,
            BatchId = entry.FormName, // 借用 BatchId 存页面名
            Status = "Success",
            Payload = entry.Operation,
            ClientIp = entry.ClientIp,
        };

        _db.ActivityLogs.Add(log);
        _db.SaveChanges();
    }

    public IReadOnlyList<OperationLogEntry> ListByEmployee(string employeeId)
    {
        return _db.ActivityLogs
            .Where(x => x.EmployeeId == employeeId && x.Action == "Behavior")
            .OrderByDescending(x => x.Timestamp)
            .Select(x => new OperationLogEntry
            {
                EmployeeId = x.EmployeeId,
                Operation = x.Payload ?? "",
                FormName = x.BatchId ?? "",
                Flow = x.Flow ?? Draftline.Core.Enums.FlowType.Pricing,
                ClientIp = x.ClientIp,
                OperatedAt = DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc).ToLocalTime(),
            })
            .ToList();
    }
}
