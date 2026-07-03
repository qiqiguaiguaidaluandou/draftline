using Draftline.Core.Models;

namespace Draftline.App.Services;

/// <summary>当前登录会话：操作员身份 + 下发配置。登录后由 LoginViewModel 写入。</summary>
public interface ISession
{
    OperatorIdentity Operator { get; }
    ClientConfig Config { get; }
    bool IsAuthenticated { get; }
    /// <summary>本次登录是否被要求先改密（初始/重置密码后）。改密成功后由调用方清除。</summary>
    bool MustChangePassword { get; set; }
    void SignIn(OperatorIdentity op, ClientConfig config, bool mustChangePassword = false);
}

public sealed class Session : ISession
{
    private OperatorIdentity? _operator;
    private ClientConfig? _config;

    public OperatorIdentity Operator => _operator ?? throw new InvalidOperationException("尚未登录。");
    public ClientConfig Config => _config ?? throw new InvalidOperationException("尚未登录。");
    public bool IsAuthenticated => _operator is not null;
    public bool MustChangePassword { get; set; }

    public void SignIn(OperatorIdentity op, ClientConfig config, bool mustChangePassword = false)
    {
        _operator = op;
        _config = config;
        MustChangePassword = mustChangePassword;
    }
}
