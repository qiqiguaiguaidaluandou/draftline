using Microsoft.EntityFrameworkCore;
using Draftline.Gateway.Stores;

namespace Draftline.Gateway.Auth;

/// <summary>
/// 管理端点授权：在 <see cref="TokenEndpointFilter"/> 之后运行，要求当前令牌工号是启用中的管理员。
/// 实时查库（非令牌内声明），管理员被降权/停用后立即失效。
/// </summary>
public sealed class AdminEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<DraftlineDbContext>();
        var empId = http.GetEmployeeId();

        var isAdmin = await db.AppUsers.AnyAsync(u => u.EmployeeId == empId && u.IsAdmin && u.IsActive);
        if (!isAdmin)
            return Results.Json(new { message = "需要管理员权限。" }, statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}
