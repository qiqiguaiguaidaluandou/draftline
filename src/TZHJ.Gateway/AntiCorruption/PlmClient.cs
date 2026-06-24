using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TZHJ.Gateway.AntiCorruption;

/// <summary>
/// PLM 取数防腐层。两个接口都 POST 一个裸的物料编码 JSON 数组（["110105148060", ...]），
/// 鉴权复用 EBS 的 JWT（同 EbsTokenProvider + Ebs:AuthScheme）。响应外层统一是
///   { "data": [...], "code": 0, "success": true, ... }。
/// 物料过多时按 PlmOptions.BatchSize 分批；URL 留空则该步骤整体跳过（返回空结果）。
/// </summary>
public sealed class PlmClient
{
    private readonly HttpClient _http;
    private readonly PlmOptions _opt;
    private readonly EbsOptions _ebs;
    private readonly EbsTokenProvider _token;
    private readonly ILogger<PlmClient> _logger;

    public PlmClient(HttpClient http, PlmOptions opt, EbsOptions ebs, EbsTokenProvider token, ILogger<PlmClient> logger)
    {
        _http = http;
        _opt = opt;
        _ebs = ebs;
        _token = token;
        _logger = logger;
    }

    /// <summary>取每个物料编码"是否在变更流程中"：itemNumber → isChange。ChangeUrl 未配置则返回空（hasChange 留空）。</summary>
    public async Task<IReadOnlyDictionary<string, bool>> FetchChangeFlagsAsync(
        IReadOnlyCollection<string> materialCodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, bool>();
        if (string.IsNullOrWhiteSpace(_opt.ChangeUrl))
        {
            _logger.LogInformation("Plm:ChangeUrl 未配置，跳过变更状态富化（hasChange 留空）。");
            return result;
        }

        foreach (var chunk in Chunk(materialCodes))
        {
            var data = await PostAsync(_opt.ChangeUrl, chunk, ct);
            foreach (var r in data.EnumerateArray())
            {
                var item = Str(r, "itemNumber");
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (r.TryGetProperty("isChange", out var v) &&
                    (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                    result[item.Trim()] = v.GetBoolean();
            }
        }
        return result;
    }

    /// <summary>取每个物料编码的图纸附件（已下载字节）：itemNumber → [(fileName, content)]。DrawingUrl 未配置则返回空。</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<(string FileName, byte[] Content)>>> FetchDrawingsAsync(
        IReadOnlyCollection<string> materialCodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyList<(string, byte[])>>();
        if (string.IsNullOrWhiteSpace(_opt.DrawingUrl))
        {
            _logger.LogInformation("Plm:DrawingUrl 未配置，跳过图纸下载（图纸标“缺失”）。");
            return result;
        }

        foreach (var chunk in Chunk(materialCodes))
        {
            var data = await PostAsync(_opt.DrawingUrl, chunk, ct);
            foreach (var r in data.EnumerateArray())
            {
                var item = Str(r, "itemNumber");
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (!r.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) continue;

                var list = new List<(string, byte[])>();
                foreach (var f in files.EnumerateArray())
                {
                    var fileName = Str(f, "fileName");
                    var fileStr = Str(f, "fileStr"); // MinIO 预签名 URL，自带签名、下载无需鉴权
                    if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(fileStr)) continue;

                    var bytes = await DownloadAsync(fileStr, fileName, ct);
                    if (bytes != null) list.Add((SafeFileName(fileName), bytes));
                }
                if (list.Count > 0) result[item.Trim()] = list;
            }
        }
        return result;
    }

    // ===== HTTP =====

    /// <summary>POST 裸物料编码数组，校验外层 success/code，返回 data 数组（已 Clone，调用方可安全枚举）。</summary>
    private async Task<JsonElement> PostAsync(string url, IReadOnlyList<string> codes, CancellationToken ct)
    {
        var bodyJson = JsonSerializer.Serialize(codes); // ["code1","code2",...]
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(_ebs.AuthScheme, _token.Create());

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("PLM 取数 HTTP {Status}：{Body}", (int)resp.StatusCode, Truncate(raw));
            throw new HttpRequestException($"PLM 取数失败 HTTP {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            var msg = Str(root, "message");
            _logger.LogError("PLM 返回 success=false：{Msg}", msg);
            throw new InvalidOperationException($"PLM 返回失败：{msg}");
        }
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return default; // 无数据

        return data.Clone();
    }

    /// <summary>下载图纸字节（GET 预签名 URL，不带鉴权头）。超大小上限或失败 → 告警并返回 null（该文件标缺失，不阻断整批）。</summary>
    private async Task<byte[]?> DownloadAsync(string fileUrl, string fileName, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PLM 图纸下载失败 HTTP {Status}：{File}", (int)resp.StatusCode, fileName);
                return null;
            }
            if (resp.Content.Headers.ContentLength is long len && len > _opt.MaxDrawingBytes)
            {
                _logger.LogWarning("PLM 图纸 {File} 大小 {Len} 超过上限 {Max}，跳过。", fileName, len, _opt.MaxDrawingBytes);
                return null;
            }
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PLM 图纸下载异常：{File}", fileName);
            return null;
        }
    }

    private IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyCollection<string> codes)
    {
        var size = _opt.BatchSize > 0 ? _opt.BatchSize : 200;
        var batch = new List<string>(size);
        foreach (var c in codes)
        {
            batch.Add(c);
            if (batch.Count == size) { yield return batch; batch = new List<string>(size); }
        }
        if (batch.Count > 0) yield return batch;
    }

    private static string SafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }

    private static string? Str(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => v.GetString(),
            _ => v.ToString(),
        };
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
