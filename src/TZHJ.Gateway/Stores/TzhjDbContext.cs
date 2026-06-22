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
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

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

        // 系统用户：工号唯一（登录账号）
        modelBuilder.Entity<AppUser>()
            .HasIndex(x => x.EmployeeId)
            .IsUnique()
            .HasDatabaseName("idx_app_user_employee");

        // 角色：名称唯一
        modelBuilder.Entity<Role>()
            .HasIndex(x => x.Name)
            .IsUnique()
            .HasDatabaseName("idx_role_name");

        // 角色含的数据范围：删角色级联删其范围；(角色,流程,组) 唯一
        modelBuilder.Entity<RolePermission>()
            .HasOne(x => x.Role)
            .WithMany(r => r.Permissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RolePermission>()
            .HasIndex(x => new { x.RoleId, x.Flow, x.GroupName })
            .IsUnique()
            .HasDatabaseName("idx_role_permission_unique");

        // 用户↔角色：删角色级联删指派；按工号查；(工号,角色) 唯一
        modelBuilder.Entity<UserRole>()
            .HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserRole>()
            .HasIndex(x => x.EmployeeId)
            .HasDatabaseName("idx_user_role_employee");
        modelBuilder.Entity<UserRole>()
            .HasIndex(x => new { x.EmployeeId, x.RoleId })
            .IsUnique()
            .HasDatabaseName("idx_user_role_unique");

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
