using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.Gateway.Auth;

/// <summary>认证 + 授权（占位）。校验账号→身份/权限→签发令牌。真接入 DHR + 权限表（路线图 B3）。</summary>
public interface IAuthService
{
    AuthResult Login(string employeeId, string password);
}

/// <summary>占位认证：移植自客户端 MockAuthGateway——任意非空账号即登录，授予两条流程权限；
/// 密码填 "fail" 模拟失败。权限本应来自权限表，此处先硬给。</summary>
public sealed class FakeAuthService : IAuthService
{
    private static readonly Dictionary<string, (string Name, string Dept, string Pos)> Known = new()
    {
        ["10086"] = ("张三", "采购部", "核价员"),
        ["10087"] = ("李四", "工艺部", "挑图员"),
    };

    private readonly ITokenService _tokens;

    public FakeAuthService(ITokenService tokens) => _tokens = tokens;

    public AuthResult Login(string employeeId, string password)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(password))
            return AuthResult.Fail("工号和密码不能为空。");

        if (string.Equals(password, "fail", StringComparison.OrdinalIgnoreCase))
            return AuthResult.Fail("（Fake）认证失败：演示用错误态。");

        var (name, dept, pos) = Known.TryGetValue(employeeId, out var info)
            ? info
            : ($"操作员{employeeId}", "采购部", "核价员");

        var identity = new OperatorIdentity
        {
            EmployeeId = employeeId,
            DisplayName = name,
            Department = dept,
            Position = pos,
            AllowedFlows = new[] { FlowType.Pricing, FlowType.DrawingSelection },
            CanOperate = true,
            CanSubmit = true,
        };

        return new AuthResult { Success = true, Operator = identity, Token = _tokens.Issue(employeeId) };
    }
}
