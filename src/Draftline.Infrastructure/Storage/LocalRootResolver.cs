namespace Draftline.Infrastructure.Storage;

/// <summary>本地数据根的默认位置与命名约定（App 启动与设置页共用，避免魔法串散落）。</summary>
public static class LocalRootResolver
{
    /// <summary>数据根固定的文件夹名；用户选位置时在其下建此子目录，命名保持一致。</summary>
    public const string DataFolderName = "Draftline_Data";

    /// <summary>默认根：我的文档\Draftline_Data（未自定义时用）。</summary>
    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DataFolderName);

    /// <summary>把用户选定的父目录拼成规范数据根：&lt;所选目录&gt;\Draftline_Data。</summary>
    public static string RootUnder(string chosenParent) =>
        Path.Combine(chosenParent, DataFolderName);
}
