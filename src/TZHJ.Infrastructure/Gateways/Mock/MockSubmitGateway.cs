using TZHJ.Core.Contracts;
using TZHJ.Infrastructure.Options;

namespace TZHJ.Infrastructure.Gateways.Mock;

/// <summary>
/// 占位回传：模拟后端执行回传（核价→SRM / 挑图→EBS）并返回审计号。
/// 按 SubmitFailureRate 可模拟整批失败，便于调失败态。真接入后替换为 HttpSubmitGateway。
/// </summary>
public sealed class MockSubmitGateway : ISubmitGateway
{
    private readonly MockOptions _options;
    private readonly Random _rng;

    public MockSubmitGateway(MockOptions options)
    {
        _options = options;
        _rng = new Random(options.Seed);
    }

    public async Task<SubmitResult> SubmitBatchAsync(SubmitRequest request, CancellationToken ct = default)
    {
        await Task.Delay(_options.SubmitDelayMs, ct);

        if (_rng.NextDouble() < _options.SubmitFailureRate)
        {
            return new SubmitResult
            {
                Success = false,
                Message = "（Mock）回传失败：演示用错误态，请重试。",
            };
        }

        var rowResults = request.Rows
            .Select(r => new SubmitRowResult { RowKey = r.RowKey, Success = true })
            .ToList();

        var target = request.Flow == Core.Enums.FlowType.Pricing ? "SRM" : "EBS";
        return new SubmitResult
        {
            Success = true,
            AuditId = $"AUDIT-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 36),
            RowResults = rowResults,
            Message = $"（Mock）已回传 {rowResults.Count} 行至 {target}。",
        };
    }
}
