using System.Net.Http.Json;
using TZHJ.Core.Contracts;
using TZHJ.Core.Contracts.Http;

namespace TZHJ.Infrastructure.Gateways.Http;

/// <summary>
/// 真 HTTP 认证网关：POST /api/auth/login。登录成功把令牌写入 <see cref="IAuthTokenStore"/>，
/// 之后的受保护请求由 <see cref="AuthTokenHandler"/> 自动带上 Bearer。
/// 约定：业务失败走 200 + success=false（与 Mock 一致）；协议错误才抛。
/// </summary>
public sealed class HttpAuthGateway : IAuthGateway
{
    private readonly HttpClient _http;
    private readonly IAuthTokenStore _tokens;

    public HttpAuthGateway(HttpClient http, IAuthTokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task<AuthResult> LoginAsync(string employeeId, string password, CancellationToken ct = default)
    {
        var request = new LoginRequest { EmployeeId = employeeId, Password = password };
        using var response = await _http.PostAsJsonAsync("/api/auth/login", request, HttpJson.Options, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AuthResult>(HttpJson.Options, ct)
                     ?? AuthResult.Fail("登录响应为空。");

        if (result.Success && !string.IsNullOrEmpty(result.Token))
            _tokens.Set(result.Token);

        return result;
    }

    public async Task<ApiResult> ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var request = new ChangePasswordRequest { OldPassword = oldPassword, NewPassword = newPassword };
        using var response = await _http.PostAsJsonAsync("/api/auth/change-password", request, HttpJson.Options, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ApiResult>(HttpJson.Options, ct)
               ?? ApiResult.Fail("改密响应为空。");
    }
}
