using Microsoft.EntityFrameworkCore;

namespace TZHJ.Gateway.Stores;

public sealed class TzhjDbContext : DbContext
{
    public TzhjDbContext(DbContextOptions<TzhjDbContext> options) : base(options)
    {
    }

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<OperationLogEntity> OperationLogs => Set<OperationLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 审计查询优化
        modelBuilder.Entity<AuditRecord>()
            .HasIndex(r => new { r.Flow, r.EmployeeId, r.WindowStart, r.WindowEnd })
            .HasDatabaseName("idx_audit_lookup");

        // 操作日志查询优化
        modelBuilder.Entity<OperationLogEntity>()
            .HasIndex(x => x.EmployeeId)
            .HasDatabaseName("idx_oplog_employee");

        modelBuilder.Entity<OperationLogEntity>()
            .HasIndex(x => x.OperatedAt)
            .HasDatabaseName("idx_oplog_time");
    }
}
