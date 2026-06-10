using TZHJ.Core.Contracts.Http;

namespace TZHJ.Gateway.Stores;

/// <summary>用户操作日志 PostgreSQL 持久化实现。</summary>
public sealed class PgOperationLogStore : IOperationLogStore
{
    private readonly TzhjDbContext _db;

    public PgOperationLogStore(TzhjDbContext db) => _db = db;

    public void Append(OperationLogEntry entry)
    {
        var entity = new OperationLogEntity
        {
            EmployeeId = entry.EmployeeId,
            Operation = entry.Operation,
            FormName = entry.FormName,
            Flow = entry.Flow,
            ClientIp = entry.ClientIp,
            OperatedAt = entry.OperatedAt.ToUniversalTime(),
        };

        _db.OperationLogs.Add(entity);
        _db.SaveChanges();
    }

    public IReadOnlyList<OperationLogEntry> ListByEmployee(string employeeId)
    {
        return _db.OperationLogs
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.OperatedAt)
            .Select(x => new OperationLogEntry
            {
                EmployeeId = x.EmployeeId,
                Operation = x.Operation,
                FormName = x.FormName,
                Flow = x.Flow,
                ClientIp = x.ClientIp,
                OperatedAt = DateTime.SpecifyKind(x.OperatedAt, DateTimeKind.Utc).ToLocalTime(),
            })
            .ToList();
    }
}
