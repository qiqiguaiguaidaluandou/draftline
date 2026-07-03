using System.Net.Http.Headers;

namespace Draftline.Infrastructure.Gateways.Http;

/// <summary>
/// 给受保护请求自动加 Authorization: Bearer {token}。
/// 登录端点未带令牌（尚未登录）——无令牌时不加头，由后端按未授权处理；登录后 token 即在。
/// </summary>
public sealed class AuthTokenHandler : DelegatingHandler
{
    private readonly IAuthTokenStore _tokens;

    public AuthTokenHandler(IAuthTokenStore tokens) => _tokens = tokens;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokens.Token;
        if (!string.IsNullOrEmpty(token) && request.Headers.Authorization is null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
