using System.Net.Http.Json;
using Draftline.Core.Contracts;
using Draftline.Core.Contracts.Http;
using Draftline.Core.Enums;

namespace Draftline.Infrastructure.Gateways.Http;

/// <summary>
/// 真 HTTP 审计查询网关：GET /api/audit/exists。身份以令牌为准；窗口起止经 :O 往返，
/// 与后端 §4 约定一致（与 /drawings 同种"流程+窗口"定位）。
/// </summary>
public sealed class HttpAuditGateway : IAuditGateway
{
    private readonly HttpClient _http;

    public HttpAuditGateway(HttpClient http) => _http = http;

    public async Task<AuditExistsResponse> ExistsAsync(
        FlowType flow, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default)
    {
        var url = $"/api/audit/exists?flow={flow}" +
                  $"&windowStart={Uri.EscapeDataString(windowStart.ToString("O"))}" +
                  $"&windowEnd={Uri.EscapeDataString(windowEnd.ToString("O"))}";

        var response = await _http.GetFromJsonAsync<AuditExistsResponse>(url, HttpJson.Options, ct);
        return response ?? new AuditExistsResponse { Exists = false };
    }
}
