namespace TZHJ.Core.Enums;

/// <summary>待填列在网格中的编辑器类型。只读字段用 <see cref="ReadOnly"/>。</summary>
public enum FieldEditor
{
    /// <summary>只读单元格（EBS / PLM 字段）。</summary>
    ReadOnly,

    /// <summary>自由文本输入。</summary>
    Text,

    /// <summary>数值输入（如核价目标价）。</summary>
    Number,

    /// <summary>下拉枚举（如挑图"是否机加中心可以做"：H06 / 否）。</summary>
    Dropdown,
}
