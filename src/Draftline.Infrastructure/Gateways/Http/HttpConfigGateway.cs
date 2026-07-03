using System.Net.Http.Json;
using Draftline.Core.Contracts;
using Draftline.Core.Models;

namespace Draftline.Infrastructure.Gateways.Http;

/// <summary>
/// 真 HTTP 配置下发网关：GET /api/config。身份以令牌为准（后端从 token 解析工号），
/// employeeId 仅作查询参数附上（后端忽略，便于排障/日志对齐）。
/// </summary>
public sealed class HttpConfigGateway : IConfigGateway
{
    private readonly HttpClient _http;

    public HttpConfigGateway(HttpClient http) => _http = http;

    public async Task<ClientConfig> GetConfigAsync(string employeeId, CancellationToken ct = default)
    {
        var url = $"/api/config?employeeId={Uri.EscapeDataString(employeeId)}";
        var config = await _http.GetFromJsonAsync<ClientConfig>(url, HttpJson.Options, ct);
        return config ?? throw new InvalidOperationException("配置下发响应为空。");
    }
}
