using Microsoft.EntityFrameworkCore;
using Draftline.Core.Enums;
using Draftline.Gateway.Auth;
using Draftline.Gateway.Stores;

namespace Draftline.Tests.Auth;

public class DbAuthServiceTests
{
    private static readonly JwtOptions Jwt = new()
    {
        Key = "unit-test-signing-key-0123456789-abcdef",
        Issuer = "Draftline.Gateway",
        Audience = "Draftline.App",
        ExpiryMinutes = 60,
    };

    private static readonly PasswordService Passwords = new();

    private static DraftlineDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<DraftlineDbContext>()
            .UseInMemoryDatabase("auth-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new DraftlineDbContext(options);
    }

    private static DbAuthService NewService(DraftlineDbContext db) =>
        new(db, Passwords, new JwtTokenService(Jwt), new PermissionService(db));

    /// <summary>给用户建一个含指定 (流程,组) 范围的角色并挂上。</summary>
    private static void SeedRole(DraftlineDbContext db, string empId, params (FlowType Flow, string Group)[] grants)
    {
        var role = new Role
        {
            Name = "role-" + Guid.NewGuid().ToString("N"),
            Permissions = grants.Select(g => new RolePermission { Flow = g.Flow, GroupName = g.Group }).ToList(),
        };
        db.Roles.Add(role);
        db.SaveChanges();
        db.UserRoles.Add(new UserRole { EmployeeId = empId, RoleId = role.Id });
        db.SaveChanges();
    }

    private static AppUser SeedUser(DraftlineDbContext db, string empId = "10086", string password = "Secret@123",
        bool active = true, bool mustChange = false)
    {
        var user = new AppUser
        {
            EmployeeId = empId,
            DisplayName = "张三",
            PasswordHash = Passwords.Hash(password),
            IsActive = active,
            MustChangePassword = mustChange,
        };
        db.AppUsers.Add(user);
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task Login_success_returns_token_and_flows_from_roles()
    {
        using var db = NewDb();
        SeedUser(db);
        SeedRole(db, "10086", (FlowType.Pricing, "组1"), (FlowType.DrawingSelection, "*"));

        var result = await NewService(db).LoginAsync("10086", "Secret@123");

        Assert.True(result.Success);
        Assert.NotNull(result.Operator);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.Contains(FlowType.Pricing, result.Operator!.AllowedFlows);
        Assert.Contains(FlowType.DrawingSelection, result.Operator.AllowedFlows);
    }

    [Fact]
    public async Task Login_with_no_permissions_succeeds_with_empty_flows()
    {
        using var db = NewDb();
        SeedUser(db);
        var result = await NewService(db).LoginAsync("10086", "Secret@123");
        Assert.True(result.Success);
        Assert.Empty(result.Operator!.AllowedFlows);
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_share_generic_message()
    {
        using var db = NewDb();
        SeedUser(db);
        var svc = NewService(db);

        var unknown = await svc.LoginAsync("99999", "whatever1");
        var wrong = await svc.LoginAsync("10086", "WrongPass1");

        Assert.False(unknown.Success);
        Assert.False(wrong.Success);
        Assert.Equal(unknown.Message, wrong.Message);
    }

    [Fact]
    public async Task Five_failures_lock_the_account()
    {
        using var db = NewDb();
        SeedUser(db);
        var svc = NewService(db);

        for (var i = 0; i < DbAuthService.MaxFailedAttempts; i++)
            await svc.LoginAsync("10086", "WrongPass1");

        // 即使密码正确，锁定期内也拒绝。
        var locked = await svc.LoginAsync("10086", "Secret@123");
        Assert.False(locked.Success);
        Assert.Contains("锁定", locked.Message);

        var user = await db.AppUsers.SingleAsync();
        Assert.NotNull(user.LockoutUntil);
    }

    [Fact]
    public async Task Success_resets_failed_attempts()
    {
        using var db = NewDb();
        SeedUser(db);
        var svc = NewService(db);

        await svc.LoginAsync("10086", "WrongPass1");
        await svc.LoginAsync("10086", "WrongPass1");
        var ok = await svc.LoginAsync("10086", "Secret@123");

        Assert.True(ok.Success);
        var user = await db.AppUsers.SingleAsync();
        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockoutUntil);
    }

    [Fact]
    public async Task Inactive_user_is_rejected()
    {
        using var db = NewDb();
        SeedUser(db, active: false);
        var result = await NewService(db).LoginAsync("10086", "Secret@123");
        Assert.False(result.Success);
        Assert.Contains("停用", result.Message);
    }

    [Fact]
    public async Task Login_surfaces_must_change_password()
    {
        using var db = NewDb();
        SeedUser(db, mustChange: true);
        var result = await NewService(db).LoginAsync("10086", "Secret@123");
        Assert.True(result.Success);
        Assert.True(result.MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_updates_hash_and_clears_flag()
    {
        using var db = NewDb();
        SeedUser(db, mustChange: true);
        var svc = NewService(db);

        var (ok, _) = await svc.ChangePasswordAsync("10086", "Secret@123", "NewSecret@456");
        Assert.True(ok);

        var user = await db.AppUsers.SingleAsync();
        Assert.False(user.MustChangePassword);
        Assert.True(Passwords.Verify(user.PasswordHash, "NewSecret@456"));

        // 新密码可登录，旧密码不可。
        Assert.True((await svc.LoginAsync("10086", "NewSecret@456")).Success);
        Assert.False((await svc.LoginAsync("10086", "Secret@123")).Success);
    }

    [Fact]
    public async Task ChangePassword_rejects_wrong_old_short_new_and_same()
    {
        using var db = NewDb();
        SeedUser(db);
        var svc = NewService(db);

        Assert.False((await svc.ChangePasswordAsync("10086", "WrongOld1", "NewSecret@456")).Ok);
        Assert.False((await svc.ChangePasswordAsync("10086", "Secret@123", "short")).Ok);
        Assert.False((await svc.ChangePasswordAsync("10086", "Secret@123", "Secret@123")).Ok);
    }
}
