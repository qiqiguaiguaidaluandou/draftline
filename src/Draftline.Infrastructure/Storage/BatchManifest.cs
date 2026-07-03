using System.Text.Json;
using System.Text.Json.Serialization;
using Draftline.Core.Enums;

namespace Draftline.Infrastructure.Storage;

/// <summary>
/// 批次 sidecar（_manifest.json）：xlsx 存字段值，manifest 存行级状态/异常原因/来源/取数时间/期望图纸清单。
/// 行级状态机存这里，批次级状态用文件夹位置表示。完整性校验 = manifest 期望图纸 vs 磁盘实际文件。
/// </summary>
public sealed class BatchManifest
{
    public FlowType Flow { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public DateTime FetchedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? AuditId { get; set; }
    public int TotalRows { get; set; } // 新增：物料总行数快照
    public List<ManifestRow> Rows { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义
    };

    public static async Task SaveAsync(string path, BatchManifest manifest, CancellationToken ct = default)
    {
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, manifest, JsonOpts, ct);
    }

    public static async Task<BatchManifest?> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BatchManifest>(fs, JsonOpts, ct);
    }
}

public sealed class ManifestRow
{
    public string RowKey { get; set; } = "";
    public string? MaterialCode { get; set; }
    public string? DisplayName { get; set; }
    public RowStatus Status { get; set; } = RowStatus.Pending;
    public string? ExceptionReason { get; set; }
    /// <summary>取数时该行附带的图纸文件名（期望存在）；完整性校验据此核对磁盘。</summary>
    public List<string> Drawings { get; set; } = new();
}
