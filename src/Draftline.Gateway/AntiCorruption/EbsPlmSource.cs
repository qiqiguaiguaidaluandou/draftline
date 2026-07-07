using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Draftline.Core.Enums;
using Draftline.Core.Schemas;

namespace Draftline.Gateway.AntiCorruption;

/// <summary>
/// 真实 EBS 取数实现（路线图 B1）。两个接口同一端点，靠 P_IFACE_CODE 区分：
///   核价 CUX_AI_DRW_COST（带 GROUP_NAME 分组）、挑图 CUX_AI_MACH_DRW（不分组）。
/// 时间窗口由调用方（DataIngestionService）按项目规则给定，这里只负责取数与字段映射。
///
/// 取到 EBS 基础数据后，按物料编码调 PLM 富化（EnrichWithPlmAsync）：填 hasChange（Y/N）、
/// 下载图纸字节挂到行上（随表格一起落进批次文件夹）。PLM URL 未配置时这一步自动跳过。
/// OpenDrawingAsync 仍返回 null——图纸在取数阶段已下载落盘，由磁盘流式服务，无控制器走此方法。
/// </summary>
public sealed class EbsPlmSource : IEbsPlmSource
{
    private readonly HttpClient _http;
    private readonly EbsOptions _opt;
    private readonly EbsTokenProvider _token;
    private readonly PlmClient _plm;
    private readonly ILogger<EbsPlmSource> _logger;

    public EbsPlmSource(HttpClient http, EbsOptions opt, EbsTokenProvider token, PlmClient plm, ILogger<EbsPlmSource> logger)
    {
        _http = http;
        _opt = opt;
        _token = token;
        _plm = plm;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SourceRow>> FetchRowsAsync(
        FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default)
    {
        var ifaceCode = flow == FlowType.Pricing ? _opt.PricingIfaceCode : _opt.DrawingIfaceCode;
        var url = flow == FlowType.Pricing ? _opt.PricingUrl : _opt.DrawingUrl;

        // P_BATCH_NUMBER = "AI" + 调用时刻时间戳；P_REQUEST_DATA 是一段 JSON 字符串。
        var batchNumber = "AI" + DateTime.Now.ToString("yyyyMMddHHmmss");
        var requestData = $"{{\"DATETIME_FROM\":\"{windowStart:yyyy-MM-dd HH:mm:ss}\",\"DATETIME_TO\":\"{windowEnd:yyyy-MM-dd HH:mm:ss}\"}}";

        var bodyJson = JsonSerializer.Serialize(new
        {
            P_BATCH_NUMBER = batchNumber,
            P_IFACE_CODE = ifaceCode,
            P_REQUEST_DATA = requestData,
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue(_opt.AuthScheme, _token.Create());

        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("EBS 取数 HTTP {Status}：{Body}", (int)resp.StatusCode, Truncate(raw));
            throw new HttpRequestException($"EBS 取数失败 HTTP {(int)resp.StatusCode}");
        }

        var dataArray = ParseEnvelope(raw, batchNumber);
        var rows = flow == FlowType.Pricing ? MapPricing(dataArray) : MapDrawing(dataArray);

        // PLM 富化：填 hasChange、下载图纸字节。两个 flow 的 materialCode/hasChange 键名相同（均为常量字符串）。
        var mcKey = flow == FlowType.Pricing ? FieldSchemas.PricingKeys.MaterialCode : FieldSchemas.DrawingKeys.MaterialCode;
        var hcKey = flow == FlowType.Pricing ? FieldSchemas.PricingKeys.HasChange : FieldSchemas.DrawingKeys.HasChange;
        await EnrichWithPlmAsync(rows, mcKey, hcKey, ct);
        return rows;
    }

    /// <summary>
    /// 按物料编码调 PLM，时序：变更检查 → 图纸附件下载 → 再次变更检查。hasChange 以第二次变更检查结果为准
    /// （Y/N，未返回的料号留空）；图纸下载字节挂到对应行。
    /// PLM 调用失败会向上抛（本批不登记、下个轮询重采）；单料号缺数据/单文件下载失败不阻断其余行。
    /// </summary>
    private async Task EnrichWithPlmAsync(IReadOnlyList<SourceRow> rows, string mcKey, string hcKey, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var codes = rows
            .Select(r => r.Values.TryGetValue(mcKey, out var c) ? c : null)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct()
            .ToList();
        if (codes.Count == 0) return;

        // 时序：变更检查 → 取图纸 → 再次变更检查。第一次变更检查按业务要求在取图前先探一次，
        // 但以第二次（取图之后）的结果为准写入 hasChange，反映取图期间可能发生的变更状态变化。
        await _plm.FetchChangeFlagsAsync(codes, ct);
        var drawings = await _plm.FetchDrawingsAsync(codes, ct);
        var changeFlags = await _plm.FetchChangeFlagsAsync(codes, ct);

        foreach (var r in rows)
        {
            if (!r.Values.TryGetValue(mcKey, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            var code = raw.Trim();

            if (changeFlags.TryGetValue(code, out var isChange))
                r.Values[hcKey] = isChange ? "Y" : "N";

            if (drawings.TryGetValue(code, out var files))
                foreach (var (fileName, content) in files)
                    r.Drawings.Add(new SourceDrawing
                    {
                        DrawingId = fileName,   // = 文件名，落盘即此名，磁盘流式端点按此查找
                        FileName = fileName,
                        MaterialCode = code,
                        Content = content,
                    });
        }
    }

    /// <summary>图纸在取数阶段已下载落盘、由磁盘流式服务，无控制器走此方法。保留接口契约，返回 null。</summary>
    public Task<byte[]?> OpenDrawingAsync(
        FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd, string drawingId, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(null);

    // ===== 解析：外层 OutputParameters → X_RETURN_CODE 判错 → X_RESPONSE_DATA(字符串)二次解析 → DATA 数组 =====

    private JsonElement ParseEnvelope(string raw, string batchNumber)
    {
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("OutputParameters", out var op))
            throw new InvalidOperationException("EBS 响应缺少 OutputParameters。");

        var returnCode = Str(op, "X_RETURN_CODE");
        if (!string.Equals(returnCode, "S", StringComparison.OrdinalIgnoreCase))
        {
            var msg = Str(op, "X_RETURN_MESG");
            _logger.LogError("EBS 返回失败 code={Code} msg={Msg} batch={Batch}", returnCode, msg, batchNumber);
            throw new InvalidOperationException($"EBS 返回非成功：code={returnCode}, msg={msg}");
        }

        var responseData = Str(op, "X_RESPONSE_DATA");
        if (string.IsNullOrWhiteSpace(responseData))
            return default; // 无数据

        // X_RESPONSE_DATA 本身是 JSON 字符串，需二次解析。clone 以便 doc 释放后仍可用。
        using var inner = JsonDocument.Parse(responseData);
        if (!inner.RootElement.TryGetProperty("DATA", out var data) || data.ValueKind != JsonValueKind.Array)
            return default;
        return data.Clone();
    }

    // ===== 字段映射（EBS 名 → 项目内部 key，见 docs/接口文档.md）=====

    private static IReadOnlyList<SourceRow> MapPricing(JsonElement data)
    {
        var rows = new List<SourceRow>();
        if (data.ValueKind != JsonValueKind.Array) return rows;

        foreach (var r in data.EnumerateArray())
        {
            var code = Str(r, "ITEM_CODE") ?? "";
            rows.Add(new SourceRow
            {
                RowKey = code,
                GroupName = Str(r, "GROUP_NAME"),   // 分组依据
                Values = new Dictionary<string, string?>
                {
                    [FieldSchemas.PricingKeys.MaterialCode] = code,
                    [FieldSchemas.PricingKeys.Model] = Str(r, "ITEM_MODEL"),
                    [FieldSchemas.PricingKeys.Name] = Str(r, "ITEM_NAME"),
                    [FieldSchemas.PricingKeys.MaterialDesc] = Str(r, "ITEM_DESC"),
                    [FieldSchemas.PricingKeys.DemandQty] = Str(r, "TTL_QTY"),
                    [FieldSchemas.PricingKeys.HasChange] = null,   // 由 PLM 富化填 Y/N（EnrichWithPlmAsync）
                    [FieldSchemas.PricingKeys.TargetPrice] = null, // 手填
                },
            });
        }
        return rows;
    }

    private static IReadOnlyList<SourceRow> MapDrawing(JsonElement data)
    {
        var rows = new List<SourceRow>();
        if (data.ValueKind != JsonValueKind.Array) return rows;

        foreach (var r in data.EnumerateArray())
        {
            var seqId = Str(r, "SEQ_ID") ?? "";
            rows.Add(new SourceRow
            {
                RowKey = seqId,
                GroupName = null, // 挑图不分组
                Values = new Dictionary<string, string?>
                {
                    [FieldSchemas.DrawingKeys.EbsId] = seqId,
                    [FieldSchemas.DrawingKeys.InvOrg] = Str(r, "ORGANIZATION_CODE"),
                    [FieldSchemas.DrawingKeys.SourceNo] = Str(r, "SOURCE_DOC_NUMBER"),
                    [FieldSchemas.DrawingKeys.Project] = Str(r, "PROJECT_CODE"),
                    [FieldSchemas.DrawingKeys.ProductLine] = Str(r, "PROD_CODE"),
                    [FieldSchemas.DrawingKeys.PlanNo] = Str(r, "INTER_PROJECT_CODE"),
                    [FieldSchemas.DrawingKeys.DeptDesc] = Str(r, "DEPARTMENT"), // 接口暂未返回 → 容错为 null
                    [FieldSchemas.DrawingKeys.MaterialCode] = Str(r, "ITEM_CODE"),
                    [FieldSchemas.DrawingKeys.MaterialDesc] = Str(r, "ITEM_DESC"),
                    [FieldSchemas.DrawingKeys.CurrentQty] = Str(r, "OFFSET_CUR_QTY"),
                    [FieldSchemas.DrawingKeys.CreateDate] = Str(r, "CREATION_DATE"),
                    [FieldSchemas.DrawingKeys.DemandDate] = Str(r, "NEED_BY_DATE"),
                    [FieldSchemas.DrawingKeys.Applicant] = Str(r, "EMPLOYEE_NAME"),
                    [FieldSchemas.DrawingKeys.Remark] = Str(r, "REMARKS"),
                    [FieldSchemas.DrawingKeys.HasChange] = null,  // 由 PLM 富化填 Y/N（EnrichWithPlmAsync）
                    [FieldSchemas.DrawingKeys.CanMachine] = null, // 手填
                },
            });
        }
        return rows;
    }

    /// <summary>安全读字符串字段：缺失/null 都返回 null；数值等非字符串也转成字符串。</summary>
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
