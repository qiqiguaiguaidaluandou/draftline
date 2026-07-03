using Draftline.Core.Models;

namespace Draftline.Core.Contracts;

/// <summary>
/// 配置下发网关：时间窗规则、字段集、网关地址、本地根、保留策略等。
/// 字段"配置化、加字段不改代码"由此落地——真接口到位前 Mock 返回默认 schema。
/// </summary>
public interface IConfigGateway
{
    Task<ClientConfig> GetConfigAsync(string employeeId, CancellationToken ct = default);
}
