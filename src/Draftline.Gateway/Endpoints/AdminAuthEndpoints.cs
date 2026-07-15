using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Draftline.Core.Logging;
using Draftline.Gateway.Auth;
using Draftline.Gateway.Stores;

namespace Draftline.Gateway.Endpoints;

/// <summary>
/// 管理后台的 Cookie 登入/登出端点（与 /api/* 的 JWT 体系相互独立）。
/// 登录复用 <see cref="IAuthService"/> 校验凭证，并额外要求"启用中的管理员"。
/// </summary>
public static class AdminAuthEndpoints
{
    public const string Scheme = "AdminCookie";

    public static void MapAdminAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/login", async (HttpContext http, IAuthService auth, DraftlineDbContext db,
            [FromForm] string employeeId, [FromForm] string password) =>
        {
            var result = await auth.LoginAsync(employeeId ?? "", password ?? "");
            var op = result.Operator;
            var isAdmin = result.Success && op is not null
                && await db.AppUsers.AnyAsync(u => u.EmployeeId == op!.EmployeeId && u.IsAdmin && u.IsActive);

            db.LogActivity(
                LogActions.AdminLogin,
                op?.EmployeeId ?? (employeeId ?? "").Trim(),
                clientIp: http.Connection.RemoteIpAddress?.ToString(),
                status: isAdmin ? "Success" : "Failed",
                payload: isAdmin ? null : (result.Success ? "非管理员，拒绝进入后台" : result.Message));
            await db.SaveChangesAsync();

            if (!isAdmin)
                return Results.Redirect("/admin/login?error=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, op!.EmployeeId),
                new(ClaimTypes.Name, op.DisplayName),
                new("draftline:isAdmin", "true"),
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
