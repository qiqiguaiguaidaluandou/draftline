using System.Net.Http.Json;
using TZHJ.Core.Contracts;

namespace TZHJ.Infrastructure.Gateways.Http;

/// <summary>
/// 真 HTTP 回传网关：POST /api/submit（整批正常行 → 后端按流程分发 SRM/EBS 并记审计）。
/// 业务失败（如回传被拒）走 200 + success=false，原样返回给 UI——与 Mock 一致。
/// </summary>
public sealed class HttpSubmitGateway : ISubmitGateway
{
    private readonly HttpClient _http;

    public HttpSubmitGateway(HttpClient http) => _http = http;

    public async Task<SubmitResult> SubmitBatchAsync(SubmitRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/submit", request, HttpJson.Options, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SubmitResult>(HttpJson.Options, ct)
               ?? new SubmitResult { Success = false, Message = "回传响应为空。" };
    }
}
