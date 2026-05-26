using System.Diagnostics;
using System.IO;

namespace TZHJ.App.Services;

/// <summary>
/// "在资源管理器中打开" / 用本机软件打开图纸。软件不内嵌看图，只提供入口。
/// </summary>
public interface IExplorerService
{
    /// <summary>在资源管理器中打开文件夹。</summary>
    void OpenFolder(string path);

    /// <summary>用系统默认关联程序打开文件（PDF 阅读器 / CAD）。</summary>
    void OpenFile(string path);
}

public sealed class ExplorerService : IExplorerService
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
