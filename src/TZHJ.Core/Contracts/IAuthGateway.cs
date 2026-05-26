namespace TZHJ.Core.Contracts;

/// <summary>
/// 认证网关。DHR/SSO 后续接入，先用 Mock 占位。客户端→后端，后端→DHR。
/// </summary>
public interface IAuthGateway
{
    Task<AuthResult> LoginAsync(string employeeId, string password, CancellationToken ct = default);
}
