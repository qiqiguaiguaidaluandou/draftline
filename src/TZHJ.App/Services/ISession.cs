using TZHJ.Core.Models;

namespace TZHJ.App.Services;

/// <summary>当前登录会话：操作员身份 + 下发配置。登录后由 LoginViewModel 写入。</summary>
public interface ISession
{
    OperatorIdentity Operator { get; }
    ClientConfig Config { get; }
    bool IsAuthenticated { get; }
    void SignIn(OperatorIdentity op, ClientConfig config);
}

public sealed class Session : ISession
{
    private OperatorIdentity? _operator;
    private ClientConfig? _config;

    public OperatorIdentity Operator => _operator ?? throw new InvalidOperationException("尚未登录。");
    public ClientConfig Config => _config ?? throw new InvalidOperationException("尚未登录。");
    public bool IsAuthenticated => _operator is not null;

    public void SignIn(OperatorIdentity op, ClientConfig config)
    {
        _operator = op;
        _config = config;
    }
}
