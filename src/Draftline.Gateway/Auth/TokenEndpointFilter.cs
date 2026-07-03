using Microsoft.Net.Http.Headers;

namespace Draftline.Gateway.Auth;

/// <summary>
/// 受保护端点的令牌校验过滤器：取 Authorization: Bearer，校验并把工号塞进 HttpContext.Items。
/// 落实"身份以 token 为准"（D2）：取数/回传据此限定本人数据，不轻信请求体里的工号。
/// </summary>
public sealed class TokenEndpointFilter : IEndpointFilter
{
    public const string EmployeeIdKey = "draftline.empId";

    private readonly ITokenService _tokens;

    public TokenEndpointFilter(ITokenService tokens) => _tokens = tokens;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var header = http.Request.Headers[HeaderNames.Authorization].ToString();
        const string scheme = "Bearer ";
        var token = header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? header[scheme.Length..].Trim() : null;

        var employeeId = _tokens.ResolveEmployeeId(token);
        if (employeeId is null)
            return Results.Json(new { message = "未授权：缺少或无效的令牌。" }, statusCode: StatusCodes.Status401Unauthorized);

        http.Items[EmployeeIdKey] = employeeId;
        return await next(context);
    }
}

public static class HttpContextExtensions
{
    /// <summary>取当前请求绑定的工号（已过 TokenEndpointFilter）。</summary>
    public static string GetEmployeeId(this HttpContext http) =>
        (string)http.Items[TokenEndpointFilter.EmployeeIdKey]!;
}
