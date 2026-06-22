using TZHJ.Core.Contracts.Http;

namespace TZHJ.Core.Contracts;

/// <summary>
/// 认证网关。账号/密码由本系统管理员维护（不接 DHR/SSO）：登录校验本地凭证、签发 JWT；改密走本人自助。
/// </summary>
public interface IAuthGateway
{
    Task<AuthResult> LoginAsync(string employeeId, string password, CancellationToken ct = default);

    /// <summary>本人改密（需已登录持有令牌）。</summary>
    Task<ApiResult> ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default);
}
