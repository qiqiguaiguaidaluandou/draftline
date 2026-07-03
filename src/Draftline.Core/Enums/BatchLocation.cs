namespace Draftline.Core.Enums;

/// <summary>
/// 批次级状态 = 所在文件夹。状态变化 = 文件夹变化，文件夹即真相源。
/// （异常待跟进是"行"的去处而非批次状态，单列在 <see cref="LocalFolders"/>。）
/// </summary>
public enum BatchLocation
{
    /// <summary>待处理：刚取到 / 处理中（动过但未整批提交）。</summary>
    Todo,

    /// <summary>已处理：整批回传成功后，整个批次目录移入。</summary>
    Done,
}
