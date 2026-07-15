using System.Net.Http.Json;
using Draftline.Core.Contracts;
using Draftline.Core.Contracts.Http;

namespace Draftline.Infrastructure.Gateways.Http;

/// <summary>
/// 真 HTTP 操作日志网关：GET /api/oplog/mine 查本人（身份以令牌为准，工号由后端过滤）。
/// </summary>
public sealed class HttpOperationLogGateway : IOperationLogGateway
{
    private readonly HttpClient _http;

    public HttpOperationLogGateway(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<OperationLogEntry>> ListMineAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetFromJsonAsync<OperationLogListResponse>("/api/oplog/mine", HttpJson.Options, ct);
        return resp?.Items ?? new List<OperationLogEntry>();
    }
}
