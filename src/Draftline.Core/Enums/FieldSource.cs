namespace Draftline.Core.Enums;

/// <summary>表单字段的数据来源。</summary>
public enum FieldSource
{
    /// <summary>EBS 取数返回的需求字段（只读）。</summary>
    Ebs,

    /// <summary>按物料编码到 PLM 取的字段，如"是否存在变更"（只读）。</summary>
    Plm,

    /// <summary>操作员在软件内手填的待填列（不从外部系统取）。</summary>
    Manual,
}
