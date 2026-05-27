namespace TZHJ.Gateway.Auth;

/// <summary>
/// 会话令牌服务（占位）。本期签发/校验自管的不透明串、内嵌工号；
/// 真接入 DHR/SSO 后换成校验其令牌（路线图 B3）。令牌不下发凭证，凭证只在后端。
/// </summary>
public interface ITokenService
{
    string Issue(string employeeId);

    /// <summary>校验令牌并取出工号；无效返回 null。</summary>
    string? ResolveEmployeeId(string? token);
}

/// <summary>占位令牌：形如 "tzhj:{工号}:{guid}"，不签名不过期——仅供骨架联调。</summary>
public sealed class FakeTokenService : ITokenService
{
    private const string Prefix = "tzhj";

    public string Issue(string employeeId) => $"{Prefix}:{employeeId}:{Guid.NewGuid():N}";

    public string? ResolveEmployeeId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split(':');
        if (parts.Length != 3 || parts[0] != Prefix || string.IsNullOrWhiteSpace(parts[1])) return null;
        return parts[1];
    }
}
