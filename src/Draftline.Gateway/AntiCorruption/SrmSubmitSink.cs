using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Draftline.Core.Contracts;
using Draftline.Core.Enums;
using Draftline.Core.Schemas;

namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// 真实回传实现（路线图 B2）。图纸核价价格 → SRM；鉴权复用 EBS 的 JWT。
/// 机加挑图 → EBS 的回传接口尚未提供，那一支委托给 FakeDataSource 占位。
///
/// 请求体固定外层：{ interfaceCode, content: { itemList: [{ itemCode, price }] } }（price 按字符串发）。
/// 响应按每条 data[].remark 逐行判定：含"失败"前缀 → 该行失败（永久，由端点单独留痕、不重试）；
/// 否则（新增成功/更新成功）→ 成功。整批调用失败（HTTP 非 2xx / 无 data）→ 抛异常，端点按可重试处理。
/// </summary>
public sealed class SrmSubmitSink : ISubmitSink
{
    private readonly HttpClient _http;
    private readonly SrmOptions _opt;
    private readonly EbsOptions _ebs;
    private readonly EbsTokenProvider _token;
    private readonly FakeDataSource _fake;
    private readonly ILogger<SrmSubmitSink> _logger;

    public SrmSubmitSink(HttpClient http, SrmOptions opt, EbsOptions ebs, EbsTokenProvider token,
        FakeDataSource fake, ILogger<SrmSubmitSink> logger)
    {
        _http = http;
        _opt = opt;
        _ebs = ebs;
        _token = token;
        _fake = fake;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubmitRowResult>> SubmitAsync(
        FlowType flow, string employeeId, IReadOnlyList<SubmitRow> rows, CancellationToken ct = default)
    {
        // 挑图→EBS 回传接口未到位，沿用占位实现。
        if (flow != FlowType.Pricing)
            return await _fake.SubmitAsync(flow, employeeId, rows, ct);

        if (rows.Count == 0) return Array.Empty<SubmitRowResult>();

        // 拼 itemList：itemCode = 物料编码（核价行主键），price = 手填目标价（字符串）。
        var itemList = rows.Select(r => new
        {
            itemCode = r.Values.GetValueOrDefault(FieldSchemas.PricingKeys.MaterialCode) ?? r.RowKey,
            price = r.Values.GetValueOrDefault(FieldSchemas.PricingKeys.TargetPrice) ?? "",
        }).ToList();

        var bodyJson = JsonSerializer.Serialize(new
        {
            interfaceCode = _opt.InterfaceCode,
            content = new { itemList },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, _opt.Url)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(_ebs.AuthScheme, _token.Create());

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("SRM 回传 HTTP {Status}：{Body}", (int)resp.StatusCode, Truncate(raw));
            throw new HttpRequestException($"SRM 回传失败 HTTP {(int)resp.StatusCode}");
        }

        // 解析 data[]：itemCode → remark。两种外层（code "S" / "200"+SUCCESS）都只认 data。
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            _logger.LogError("SRM 回传响应缺少 data 数组：{Body}", Truncate(raw));
            throw new InvalidOperationException("SRM 回传响应无 data，无法判定逐行结果。");
        }

        var remarkByItem = new Dictionary<string, string?>();
        foreach (var d in data.EnumerateArray())
        {
            var itemCode = Str(d, "itemCode");
            if (!string.IsNullOrWhiteSpace(itemCode))
                remarkByItem[itemCode.Trim()] = Str(d, "remark");
        }

        // 逐行映射回 RowKey：含"失败"前缀 → 失败；未返回该行 → 失败（留痕）；否则成功。
        return rows.Select(r =>
        {
            var itemCode = (r.Values.GetValueOrDefault(FieldSchemas.PricingKeys.MaterialCode) ?? r.RowKey).Trim();
            if (!remarkByItem.TryGetValue(itemCode, out var remark))
                return new SubmitRowResult { RowKey = r.RowKey, Success = false, Message = "SRM 未返回该行结果" };

            var success = remark != null && !remark.TrimStart().StartsWith("失败");
            return new SubmitRowResult { RowKey = r.RowKey, Success = success, Message = remark };
        }).ToList();
    }

    /// <summary>真实回传不做随机整批失败模拟（占位演示用），始终 false。整批故障靠 SubmitAsync 抛异常表达。</summary>
    public bool ShouldFailBatch() => false;

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
