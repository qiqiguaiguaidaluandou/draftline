using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Draftline.Core.Contracts;
using Draftline.Core.Enums;
using Draftline.Core.Schemas;

namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// 真实回传实现（路线图 B2）。按流程分派到不同外部系统：
///   核价（Pricing）→ SRM 价格接口（逐行 remark 判定）；
///   挑图（DrawingSelection）→ EBS 机加结果回传接口 CUX_AI_MACH_DRW_RST（批次级 X_RETURN_CODE 判定）。
/// 两支鉴权都复用 EBS 的 JWT（<see cref="EbsTokenProvider"/> + <see cref="EbsOptions.AuthScheme"/>）。
///
/// SRM 请求体：{ interfaceCode, content: { itemList: [{ itemCode, price }] } }（price 按字符串发）；
/// EBS 请求体：{ P_BATCH_NUMBER, P_IFACE_CODE, P_REQUEST_DATA }，其中 P_REQUEST_DATA 是**转义的 JSON 字符串**
/// （数组元素 { SEQ_ID:number, ORG_CODE:string }）。SEQ_ID←EBS-ID；ORG_CODE←是否机加中心可以做（H06 原样，「否」→"N"）。
///
/// 失败语义（两支一致地映射为 ISubmitSink 契约的「逐行结果」+「抛异常＝整批可重试」）：
///   SRM：逐行 remark 含"失败"前缀 → 该行永久失败；整批 HTTP/解析异常 → 抛异常（端点可重试）。
///   EBS：X_RETURN_CODE=S → 全部成功；=E 且 X_RETURN_MESG 含「部分失败」→ X_RESPONSE_DATA 里的行永久失败、
///        其余成功（部分落库）；=E 其余情形 → 抛异常（整批回滚、未落库、可重试）。注意：任何 E 的 X_RESPONSE_DATA
///        都会带上未成功的数据，故判据是 X_RETURN_MESG 而非「是否带数据」。
/// </summary>
public sealed class RemoteSubmitSink : ISubmitSink
{
    private readonly HttpClient _http;
    private readonly SrmOptions _opt;
    private readonly EbsOptions _ebs;
    private readonly EbsTokenProvider _token;
    private readonly ILogger<RemoteSubmitSink> _logger;

    public RemoteSubmitSink(HttpClient http, SrmOptions opt, EbsOptions ebs, EbsTokenProvider token,
        ILogger<RemoteSubmitSink> logger)
    {
        _http = http;
        _opt = opt;
        _ebs = ebs;
        _token = token;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubmitRowResult>> SubmitAsync(
        FlowType flow, string employeeId, IReadOnlyList<SubmitRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return Array.Empty<SubmitRowResult>();

        return flow == FlowType.Pricing
            ? await SubmitPricingToSrmAsync(rows, ct)
            : await SubmitDrawingToEbsAsync(rows, ct);
    }

    // —— 核价 → SRM ——
    private async Task<IReadOnlyList<SubmitRowResult>> SubmitPricingToSrmAsync(
        IReadOnlyList<SubmitRow> rows, CancellationToken ct)
    {
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

        var raw = await PostAsync(_opt.Url, bodyJson, "SRM 回传", ct);

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

    // —— 挑图 → EBS 机加结果回传 ——
    private async Task<IReadOnlyList<SubmitRowResult>> SubmitDrawingToEbsAsync(
        IReadOnlyList<SubmitRow> rows, CancellationToken ct)
    {
        // P_REQUEST_DATA 是转义的 JSON 字符串：数组元素 { SEQ_ID, ORG_CODE }。
        var payload = rows.Select(r => new Dictionary<string, object?>
        {
            ["SEQ_ID"] = SeqId(r),
            ["ORG_CODE"] = MapOrgCode(r.Values.GetValueOrDefault(FieldSchemas.DrawingKeys.CanMachine)),
        }).ToList();

        var bodyJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["P_BATCH_NUMBER"] = "AI" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["P_IFACE_CODE"] = _ebs.DrawingResultIfaceCode,
            ["P_REQUEST_DATA"] = JsonSerializer.Serialize(payload),
        });

        var raw = await PostAsync(_ebs.DrawingResultUrl, bodyJson, "EBS 挑图回传", ct);
        return MapDrawingResult(rows, raw, _logger);
    }

    /// <summary>
    /// 把 EBS 批次级响应映射为逐行结果。可测：不依赖 HttpClient。
    /// S → 全成功；E+异常行 → 那些行失败、其余成功；E+无可用异常数据 → 抛异常（整批可重试）。
    /// </summary>
    internal static IReadOnlyList<SubmitRowResult> MapDrawingResult(
        IReadOnlyList<SubmitRow> rows, string raw, ILogger? logger = null)
    {
        using var doc = JsonDocument.Parse(raw);
        // 结果在 OutputParameters 下；容错：个别环境可能直接平铺在根。
        var op = doc.RootElement.TryGetProperty("OutputParameters", out var o) && o.ValueKind == JsonValueKind.Object
            ? o
            : doc.RootElement;

        var code = Str(op, "X_RETURN_CODE")?.Trim();
        var mesg = Str(op, "X_RETURN_MESG");

        if (string.Equals(code, "S", StringComparison.OrdinalIgnoreCase))
            return rows.Select(r => new SubmitRowResult { RowKey = r.RowKey, Success = true, Message = mesg }).ToList();

        // 非 S（E）→ 失败。规则（对方明确）：任何 E 的 X_RESPONSE_DATA 都会带上未成功的数据，
        // 但**只有**「数据更新部分失败」这一类，才把 X_RESPONSE_DATA 里的行当逐行失败入异常池、其余行算已落库成功；
        // 其余任何 E 一律整批回滚（未落库）→ 抛异常让端点置为可重试。故判据是 X_RETURN_MESG，而非「是否带数据」。
        if (mesg == null || !mesg.Contains("部分失败"))
        {
            logger?.LogError("EBS 挑图回传返回 {Code}（非部分失败，整批回滚、可重试）：{Mesg}", code ?? "(空)", Truncate(mesg ?? ""));
            throw new InvalidOperationException($"EBS 挑图回传失败（{code ?? "无返回码"}）：{mesg}");
        }

        // 部分失败：X_RESPONSE_DATA 带回未成功的行 → 仅这些行进异常池，其余视为已落库成功。
        var failed = op.TryGetProperty("X_RESPONSE_DATA", out var resp) ? ExtractFailedSeqIds(resp) : null;
        if (failed == null)
        {
            // 声明部分失败却解析不出失败行 → 无法安全分流 → 保守起见整批回滚、可重试（不置已处理）。
            logger?.LogError("EBS 挑图回传声明部分失败，但 X_RESPONSE_DATA 无可解析失败行：{Mesg}", Truncate(mesg));
            throw new InvalidOperationException($"EBS 挑图回传部分失败但无法解析失败行：{mesg}");
        }

        return rows.Select(r =>
        {
            var isFailed = failed.Contains(SeqIdStr(r));
            return new SubmitRowResult
            {
                RowKey = r.RowKey,
                Success = !isFailed,
                Message = isFailed ? mesg : null,
            };
        }).ToList();
    }

    /// <summary>解析 X_RESPONSE_DATA 中的失败 SEQ_ID 集合；无可用数据时返回 null（=系统性失败信号）。</summary>
    private static HashSet<string>? ExtractFailedSeqIds(JsonElement resp)
    {
        // 容错两种形态：① 转义 JSON 字符串（同 P_REQUEST_DATA）；② 直接就是数组/对象。
        if (resp.ValueKind == JsonValueKind.Null || resp.ValueKind == JsonValueKind.Undefined)
            return null;

        if (resp.ValueKind == JsonValueKind.String)
        {
            var s = resp.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            try
            {
                using var inner = JsonDocument.Parse(s);
                return CollectSeqIds(inner.RootElement);
            }
            catch (JsonException)
            {
                return null; // 无法解析 → 当作无可用逐行数据 → 系统性失败
            }
        }

        return CollectSeqIds(resp);
    }

    private static HashSet<string>? CollectSeqIds(JsonElement el)
    {
        var set = new HashSet<string>();
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                AddSeqId(set, item);
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            AddSeqId(set, el);
        }
        return set.Count > 0 ? set : null;
    }

    private static void AddSeqId(HashSet<string> set, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("SEQ_ID", out var v))
            return;
        var s = v.ValueKind switch
        {
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.String => v.GetString(),
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
    }

    /// <summary>SEQ_ID：数值则发数字，否则发字符串（容错非纯数字的 EBS-ID）。</summary>
    private static object SeqId(SubmitRow r)
    {
        var s = SeqIdStr(r);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : s;
    }

    private static string SeqIdStr(SubmitRow r) =>
        (r.Values.GetValueOrDefault(FieldSchemas.DrawingKeys.EbsId) ?? r.RowKey ?? "").Trim();

    /// <summary>ORG_CODE：机加中心组织码原样（如 H06）；「否」→"N"。</summary>
    private static string MapOrgCode(string? canMachine)
    {
        var v = (canMachine ?? "").Trim();
        return v == "否" ? "N" : v;
    }

    private async Task<string> PostAsync(string url, string bodyJson, string what, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(_ebs.AuthScheme, _token.Create());

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("{What} HTTP {Status}：{Body}", what, (int)resp.StatusCode, Truncate(raw));
            throw new HttpRequestException($"{what}失败 HTTP {(int)resp.StatusCode}");
        }
        return raw;
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
