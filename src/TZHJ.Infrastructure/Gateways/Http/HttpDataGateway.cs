using System.Net;
using System.Net.Http.Json;
using TZHJ.Core.Contracts;
using TZHJ.Core.Contracts.Http;
using TZHJ.Core.Enums;

namespace TZHJ.Infrastructure.Gateways.Http;

/// <summary>
/// 真 HTTP 取数网关（两阶段，见开发文档 §4.2）：
/// ① POST /api/fetch 拿行 + 图纸**元数据**（无字节）；
/// ② 对每行每张图纸 GET /api/drawings 流式下载字节；
/// ③ 拼成与 Mock **完全同形**的 FetchResult 返回——LocalBatchStore 及以上全不变。
/// </summary>
public sealed class HttpDataGateway : IDataGateway
{
    private readonly HttpClient _http;

    public HttpDataGateway(HttpClient http) => _http = http;

    public async Task<FetchResult> FetchBatchAsync(FetchRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/fetch", request, HttpJson.Options, ct);
        response.EnsureSuccessStatusCode();

        var fetch = await response.Content.ReadFromJsonAsync<FetchResponse>(HttpJson.Options, ct)
                    ?? throw new InvalidOperationException("取数响应为空。");

        if (!fetch.Success)
        {
            return new FetchResult
            {
                Success = false,
                Flow = request.Flow,
                EmployeeId = request.EmployeeId,
                WindowStart = request.WindowStart,
                WindowEnd = request.WindowEnd,
                Message = fetch.Message,
            };
        }

        var rows = new List<FetchedRow>(fetch.Rows.Count);
        foreach (var row in fetch.Rows)
        {
            var drawings = new List<FetchedDrawing>(row.Drawings.Count);
            foreach (var meta in row.Drawings)
            {
                var bytes = await DownloadDrawingAsync(fetch.Flow, fetch.WindowStart, fetch.WindowEnd, meta.DrawingId, ct);
                if (bytes is null) continue; // 404 → 按"图纸缺失"处理，与空清单一致
                drawings.Add(new FetchedDrawing
                {
                    FileName = meta.FileName,
                    MaterialCode = meta.MaterialCode,
                    Content = bytes,
                });
            }

            rows.Add(new FetchedRow { RowKey = row.RowKey, Values = row.Values, Drawings = drawings });
        }

        return new FetchResult
        {
            Success = true,
            Flow = fetch.Flow,
            EmployeeId = fetch.EmployeeId,
            WindowStart = fetch.WindowStart,
            WindowEnd = fetch.WindowEnd,
            Rows = rows,
            Message = fetch.Message,
        };
    }

    /// <summary>流式下载单张图纸字节。窗口起止用 FetchResponse 回显值，保证与后端确定性重生同种子。</summary>
    private async Task<byte[]?> DownloadDrawingAsync(
        FlowType flow, DateTime windowStart, DateTime windowEnd, string drawingId, CancellationToken ct)
    {
        var url = $"/api/drawings?flow={flow}" +
                  $"&windowStart={Uri.EscapeDataString(windowStart.ToString("O"))}" +
                  $"&windowEnd={Uri.EscapeDataString(windowEnd.ToString("O"))}" +
                  $"&drawingId={Uri.EscapeDataString(drawingId)}";

        using var response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
