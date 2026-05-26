using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Infrastructure.Options;

namespace TZHJ.Infrastructure.Gateways.Mock;

/// <summary>
/// 占位认证：任意非空工号+密码即登录成功，授予两条流程权限。真接入 DHR/SSO 时替换为 HttpAuthGateway。
/// 约定：密码填 "fail" 可模拟登录失败，便于调错误态。
/// </summary>
public sealed class MockAuthGateway : IAuthGateway
{
    private static readonly Dictionary<string, (string Name, string Dept, string Pos)> KnownOperators = new()
    {
        ["10086"] = ("张三", "采购部", "核价员"),
        ["10087"] = ("李四", "工艺部", "挑图员"),
    };

    private readonly MockOptions _options;

    public MockAuthGateway(MockOptions options) => _options = options;

    public async Task<AuthResult> LoginAsync(string employeeId, string password, CancellationToken ct = default)
    {
        await Task.Delay(_options.LoginDelayMs, ct);

        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(password))
            return AuthResult.Fail("工号和密码不能为空。");

        if (string.Equals(password, "fail", StringComparison.OrdinalIgnoreCase))
            return AuthResult.Fail("（Mock）认证失败：演示用错误态。");

        var (name, dept, pos) = KnownOperators.TryGetValue(employeeId, out var info)
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

        return new AuthResult
        {
            Success = true,
            Operator = identity,
            Token = $"mock-token-{employeeId}-{Guid.NewGuid():N}",
        };
    }
}
