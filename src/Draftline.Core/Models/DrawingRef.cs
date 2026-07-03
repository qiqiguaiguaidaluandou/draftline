namespace Draftline.Core.Models;

/// <summary>
/// 一张图纸的引用。图纸平铺在批次目录、文件名带物料编码前缀；
/// 软件不内嵌看图，只提供"有无标识"与"在资源管理器中打开"。
/// </summary>
public sealed class DrawingRef
{
    /// <summary>文件名（含物料编码前缀，如 M-10231__支架A.pdf）。</summary>
    public required string FileName { get; init; }

    /// <summary>所属物料编码。</summary>
    public required string MaterialCode { get; init; }

    /// <summary>格式标签（pdf / step / …），取自扩展名，大写显示。</summary>
    public required string Kind { get; init; }

    /// <summary>完整性校验：文件当前是否真实存在于批次目录。</summary>
    public bool Exists { get; init; } = true;
}
