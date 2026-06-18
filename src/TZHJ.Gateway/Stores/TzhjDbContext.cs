using Microsoft.EntityFrameworkCore;

namespace TZHJ.Gateway.Stores;

public sealed class TzhjDbContext : DbContext
{
    public TzhjDbContext(DbContextOptions<TzhjDbContext> options) : base(options)
    {
    }

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<BatchRegistry> BatchRegistries => Set<BatchRegistry>();
    public DbSet<ExceptionEntity> Exceptions => Set<ExceptionEntity>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 联合主键：一个批次在（流程+组）维度下唯一
        modelBuilder.Entity<BatchRegistry>()
            .HasKey(x => new { x.BatchId, x.Flow, x.GroupName });

        // 批次注册查询优化
        modelBuilder.Entity<BatchRegistry>()
            .HasIndex(x => new { x.Flow, x.GroupName, x.Status })
            .HasDatabaseName("idx_batch_registry_lookup");

        // 异常池查询优化
        modelBuilder.Entity<ExceptionEntity>()
            .HasIndex(x => new { x.Flow, x.GroupName, x.Status })
            .HasDatabaseName("idx_exception_lookup");

        // 用户权限查询优化
        modelBuilder.Entity<UserPermission>()
            .HasIndex(x => x.EmployeeId)
            .HasDatabaseName("idx_user_permission_employee");

        // 统一日志查询优化
        modelBuilder.Entity<ActivityLog>()
            .HasIndex(x => new { x.EmployeeId, x.Timestamp })
            .HasDatabaseName("idx_activity_log_user_time");

        modelBuilder.Entity<ActivityLog>()
            .HasIndex(x => new { x.BatchId, x.Action })
            .HasDatabaseName("idx_activity_log_batch_action");

        // 补拉判据：按 (工号, 动作, 窗口起) 精确查回传记录（009）
        modelBuilder.Entity<ActivityLog>()
            .HasIndex(x => new { x.EmployeeId, x.Action, x.WindowStart })
            .HasDatabaseName("idx_activity_log_audit_lookup");
    }
}
