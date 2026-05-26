using TZHJ.Core.Contracts;
using TZHJ.Core.Models;
using TZHJ.Core.Schemas;
using TZHJ.Infrastructure.Options;

namespace TZHJ.Infrastructure.Gateways.Mock;

/// <summary>
/// 占位配置下发：返回内置默认时间窗 + 默认字段 schema + 本地根。真接入后替换为 HttpConfigGateway。
/// </summary>
public sealed class MockConfigGateway : IConfigGateway
{
    private readonly MockOptions _options;

    public MockConfigGateway(MockOptions options) => _options = options;

    public Task<ClientConfig> GetConfigAsync(string employeeId, CancellationToken ct = default)
    {
        var config = new ClientConfig
        {
            LocalRoot = _options.LocalRoot,
            GatewayBaseUrl = "http://localhost:8080",
            PricingWindows = CollectionSchedules.Pricing,
            DrawingSelectionWindows = CollectionSchedules.DrawingSelection,
            PricingFields = FieldSchemas.Pricing,
            DrawingSelectionFields = FieldSchemas.DrawingSelection,
            RetentionDaysForDone = 30,
        };
        return Task.FromResult(config);
    }
}
