using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TZHJ.Gateway.Auth;
using TZHJ.Gateway.Stores;

namespace TZHJ.Gateway.Endpoints;

/// <summary>
/// 管理后台的 Cookie 登入/登出端点（与 /api/* 的 JWT 体系相互独立）。
/// 登录复用 <see cref="IAuthService"/> 校验凭证，并额外要求"启用中的管理员"。
/// </summary>
public static class AdminAuthEndpoints
{
    public const string Scheme = "AdminCookie";

    public static void MapAdminAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/login", async (HttpContext http, IAuthService auth, TzhjDbContext db,
            [FromForm] string employeeId, [FromForm] string password) =>
        {
            var result = await auth.LoginAsync(employeeId ?? "", password ?? "");
            var op = result.Operator;
            var isAdmin = result.Success && op is not null
                && await db.AppUsers.AnyAsync(u => u.EmployeeId == op!.EmployeeId && u.IsAdmin && u.IsActive);

            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "AdminLogin",
                EmployeeId = op?.EmployeeId ?? (employeeId ?? "").Trim(),
                Status = isAdmin ? "Success" : "Failed",
                Payload = isAdmin ? null : (result.Success ? "非管理员，拒绝进入后台" : result.Message),
                ClientIp = http.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            if (!isAdmin)
                return Results.Redirect("/admin/login?error=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, op!.EmployeeId),
                new(ClaimTypes.Name, op.DisplayName),
                new("tzhj:isAdmin", "true"),
            };
            var identity = new ClaimsIdentity(claims, Scheme);
            await http.SignInAsync(Scheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false });

            return Results.Redirect("/admin/users");
        }).DisableAntiforgery();

        app.MapPost("/admin/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(Scheme);
            return Results.Redirect("/admin/login");
        }).DisableAntiforgery();
    }
}
