using TZHJ.Core.Enums;

namespace TZHJ.Core.Models;

/// <summary>
/// 表格中的一行（一个料号）。字段值放在 <see cref="Values"/> 字典里（key = <see cref="FieldDefinition.Key"/>），
/// 以支持配置化字段集。表格本体就是批次目录里的 清单表格.xlsx，本类是它的内存投影。
/// </summary>
public sealed class MaterialRow
{
    /// <summary>行标识键的值（核价=物料编码、挑图=EBS-ID）。回传时作关联键。</summary>
    public required string RowKey { get; set; }

    /// <summary>所属业务组（用于 Remote-First 精确定位）。</summary>
    public string? GroupName { get; set; }

    /// <summary>字段值，key = <see cref="FieldDefinition.Key"/>。只读字段由取数填入，待填列由操作员填。</summary>
    public Dictionary<string, string?> Values { get; init; } = new();

    /// <summary>该行关联的图纸（按物料编码）。</summary>
    public List<DrawingRef> Drawings { get; init; } = new();

    /// <summary>行级状态（待处理 / 已处理 / 挂起异常 / 已上传）。</summary>
    public RowStatus Status { get; set; } = RowStatus.Pending;

    /// <summary>挂起异常时记录的原因（如"图纸缺失""价格待定""图纸版本不符"）。</summary>
    public string? ExceptionReason { get; set; }

    /// <summary>取值快捷方法。</summary>
    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string? value) => Values[key] = value;
}
