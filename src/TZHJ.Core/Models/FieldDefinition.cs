using TZHJ.Core.Enums;

namespace TZHJ.Core.Models;

/// <summary>
/// 一个表单字段的定义。字段集做成配置化——"加字段不改代码"：
/// 表单列、xlsx 列、网格列都由 <see cref="FieldDefinition"/> 列表驱动。
/// 首批字段见 <see cref="Schemas.FieldSchemas"/>，将来由后端 IConfigGateway 下发。
/// </summary>
public sealed class FieldDefinition
{
    /// <summary>列键（稳定标识，用作 xlsx 列头 / 行值字典的 key）。</summary>
    public required string Key { get; init; }

    /// <summary>界面显示名（如"物料编码""目标价"）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>数据来源（EBS / PLM / 操作员手填）。</summary>
    public required FieldSource Source { get; init; }

    /// <summary>编辑器类型；只读字段为 <see cref="FieldEditor.ReadOnly"/>。</summary>
    public FieldEditor Editor { get; init; } = FieldEditor.ReadOnly;

    /// <summary>是否必填（仅对待填列有意义，回传前校验，未填不得提交该行）。</summary>
    public bool IsRequired { get; init; }

    /// <summary>下拉选项（仅 <see cref="FieldEditor.Dropdown"/> 用，如 ["是","否"]）。</summary>
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

    /// <summary>列顺序。</summary>
    public int Order { get; init; }

    /// <summary>是否作为行标识键（核价=物料编码、挑图=EBS-ID）。回传时作关联键。</summary>
    public bool IsRowKey { get; init; }

    /// <summary>是否可编辑（来源为手填且编辑器非只读）。</summary>
    public bool IsEditable => Source == FieldSource.Manual && Editor != FieldEditor.ReadOnly;
}
