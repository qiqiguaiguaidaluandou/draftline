namespace Draftline.Infrastructure.Storage;

/// <summary>本地数据目录整体迁移：把 <c>from</c> 的内容并入 <c>to</c> 后删除 <c>from</c>。</summary>
public static class DirectoryMigrator
{
    /// <summary>
    /// 把 <paramref name="from"/> 整体迁到 <paramref name="to"/>：
    /// 同卷用 <see cref="Directory.Move(string,string)"/> 快速改名；跨卷（如 C 盘 → D 盘，
    /// Directory.Move 会抛 IOException）退回递归复制 + 删源。目标已存在则合并、同名文件覆盖。
    /// from 不存在或与 to 同一目录时静默返回（幂等，便于启动期重试）。
    /// </summary>
    public static void Move(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return;
        if (!Directory.Exists(from)) return;
        if (PathEquals(from, to)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to).TrimEnd(Path.DirectorySeparatorChar))
                                  ?? to);

        // 目标不存在时优先整体改名（同卷近乎瞬时）；跨卷或目标已存在则走复制合并。
        if (!Directory.Exists(to))
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException)
            {
                // 跨卷：Directory.Move 不支持，落到复制分支。
            }
        }

        CopyMerge(from, to);
        Directory.Delete(from, recursive: true);
    }

    private static void CopyMerge(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
