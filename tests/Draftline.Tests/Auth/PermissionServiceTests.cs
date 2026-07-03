using Microsoft.EntityFrameworkCore;
using Draftline.Core.Enums;
using Draftline.Gateway.Auth;
using Draftline.Gateway.Stores;

namespace Draftline.Tests.Auth;

public class PermissionServiceTests
{
    private static DraftlineDbContext NewDb() =>
        new(new DbContextOptionsBuilder<DraftlineDbContext>()
            .UseInMemoryDatabase("perm-" + Guid.NewGuid().ToString("N")).Options);

    private static int SeedRole(DraftlineDbContext db, string name, params (FlowType Flow, string Group)[] grants)
    {
        var role = new Role
        {
            Name = name,
            Permissions = grants.Select(g => new RolePermission { Flow = g.Flow, GroupName = g.Group }).ToList(),
        };
        db.Roles.Add(role);
        db.SaveChanges();
        return role.Id;
    }

    private static void Assign(DraftlineDbContext db, string empId, params int[] roleIds)
    {
        foreach (var id in roleIds) db.UserRoles.Add(new UserRole { EmployeeId = empId, RoleId = id });
        db.SaveChanges();
    }

    [Fact]
    public async Task No_roles_means_no_grants()
    {
        using var db = NewDb();
        var svc = new PermissionService(db);
        Assert.Empty(await svc.GetGrantsAsync("10086"));
        Assert.False(await svc.HasAccessAsync("10086", FlowType.Pricing, "组1"));
    }

    [Fact]
    public async Task Single_role_expands_to_its_ranges()
    {
        using var db = NewDb();
        var rid = SeedRole(db, "核价员-组1", (FlowType.Pricing, "组1"));
        Assign(db, "10086", rid);
        var svc = new PermissionService(db);

        var grants = await svc.GetGrantsAsync("10086");
        Assert.Single(grants);
        Assert.True(await svc.HasAccessAsync("10086", FlowType.Pricing, "组1"));
        Assert.False(await svc.HasAccessAsync("10086", FlowType.Pricing, "组2"));
        Assert.False(await svc.HasAccessAsync("10086", FlowType.DrawingSelection, "组1"));
    }

    [Fact]
    public async Task Multiple_roles_union_their_ranges()
    {
        using var db = NewDb();
        var a = SeedRole(db, "核价员-组1", (FlowType.Pricing, "组1"));
        var b = SeedRole(db, "挑图员-组2", (FlowType.DrawingSelection, "组2"));
        Assign(db, "10086", a, b);
        var svc = new PermissionService(db);

        var flows = await svc.GetFlowsAsync("10086");
        Assert.Contains(FlowType.Pricing, flows);
        Assert.Contains(FlowType.DrawingSelection, flows);
        Assert.True(await svc.HasAccessAsync("10086", FlowType.Pricing, "组1"));
        Assert.True(await svc.HasAccessAsync("10086", FlowType.DrawingSelection, "组2"));
    }

    [Fact]
    public async Task Wildcard_group_grants_all_groups_in_that_flow()
    {
        using var db = NewDb();
        var rid = SeedRole(db, "核价员-全部", (FlowType.Pricing, "*"));
        Assign(db, "10086", rid);
        var svc = new PermissionService(db);

        Assert.True(await svc.HasAccessAsync("10086", FlowType.Pricing, "组1"));
        Assert.True(await svc.HasAccessAsync("10086", FlowType.Pricing, "随便什么组"));
        Assert.False(await svc.HasAccessAsync("10086", FlowType.DrawingSelection, "组1"));
    }

    [Fact]
    public async Task Overlapping_roles_deduplicate_grants()
    {
        using var db = NewDb();
        var a = SeedRole(db, "角色A", (FlowType.Pricing, "组1"));
        var b = SeedRole(db, "角色B", (FlowType.Pricing, "组1"), (FlowType.Pricing, "组2"));
        Assign(db, "10086", a, b);
        var svc = new PermissionService(db);

        var grants = await svc.GetGrantsAsync("10086");
        Assert.Equal(2, grants.Count); // (Pricing,组1) 去重，(Pricing,组2) 各一条
    }
}
