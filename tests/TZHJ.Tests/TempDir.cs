namespace TZHJ.Tests;

/// <summary>测试用临时目录：每个用例一个独立目录，Dispose 时整树删除。</summary>
internal static class TempDir
{
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tzhj-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* 测试收尾，删不掉也不让它影响结果 */ }
    }
}
