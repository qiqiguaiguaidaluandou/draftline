namespace TZHJ.Infrastructure.Gateways.Http;

/// <summary>
/// 当前会话的 Bearer 令牌持有者（进程内单例）。
/// <see cref="HttpAuthGateway"/> 登录成功后写入；<see cref="AuthTokenHandler"/> 给后续受保护请求带上。
/// 放在 Infrastructure 而非 App 的 ISession：令牌处理器在 Infrastructure，不能反向依赖 App；
/// 且令牌须在"登录响应到手"与"取配置(受保护)"之间就绪，早于 ISession.SignIn。
/// </summary>
public interface IAuthTokenStore
{
    string? Token { get; }
    void Set(string? token);
    void Clear();
}

/// <summary>线程安全的简单令牌持有者。</summary>
public sealed class AuthTokenStore : IAuthTokenStore
{
    private volatile string? _token;

    public string? Token => _token;
    public void Set(string? token) => _token = token;
    public void Clear() => _token = null;
}
