using Draftline.Infrastructure.Storage;

namespace Draftline.Tests.Storage;

public class DirectoryMigratorTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Move_relocates_all_files_and_removes_source()
    {
        var baseDir = NewTempDir();
        try
        {
            var from = Path.Combine(baseDir, "old");
            var to = Path.Combine(baseDir, "new");
            Directory.CreateDirectory(Path.Combine(from, "核价", "待处理", "组1", "20260101_0900-1000"));
            File.WriteAllText(Path.Combine(from, "核价", "待处理", "组1", "20260101_0900-1000", "a.json"), "x");
            File.WriteAllText(Path.Combine(from, "root.txt"), "y");

            DirectoryMigrator.Move(from, to);

            Assert.False(Directory.Exists(from));
            Assert.True(File.Exists(Path.Combine(to, "核价", "待处理", "组1", "20260101_0900-1000", "a.json")));
            Assert.True(File.Exists(Path.Combine(to, "root.txt")));
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }

    [Fact]
    public void Move_merges_into_existing_target_and_overwrites_same_name()
    {
        var baseDir = NewTempDir();
        try
        {
            var from = Path.Combine(baseDir, "old");
            var to = Path.Combine(baseDir, "new");
            Directory.CreateDirectory(from);
            Directory.CreateDirectory(to);
            File.WriteAllText(Path.Combine(from, "shared.txt"), "new-content");
            File.WriteAllText(Path.Combine(from, "only-old.txt"), "o");
            File.WriteAllText(Path.Combine(to, "shared.txt"), "old-content");
            File.WriteAllText(Path.Combine(to, "only-new.txt"), "n");

            DirectoryMigrator.Move(from, to);

            Assert.False(Directory.Exists(from));
            Assert.Equal("new-content", File.ReadAllText(Path.Combine(to, "shared.txt"))); // 同名覆盖
            Assert.True(File.Exists(Path.Combine(to, "only-old.txt")));
            Assert.True(File.Exists(Path.Combine(to, "only-new.txt")));                    // 保留目标原有
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }

    [Fact]
    public void Move_is_noop_when_source_missing_or_same_path()
    {
        var baseDir = NewTempDir();
        try
        {
            var missing = Path.Combine(baseDir, "nope");
            DirectoryMigrator.Move(missing, Path.Combine(baseDir, "new")); // 源不存在：静默
            Assert.False(Directory.Exists(Path.Combine(baseDir, "new")));

            var same = Path.Combine(baseDir, "same");
            Directory.CreateDirectory(same);
            File.WriteAllText(Path.Combine(same, "f.txt"), "z");
            DirectoryMigrator.Move(same, same); // 同路径：不删源
            Assert.True(File.Exists(Path.Combine(same, "f.txt")));
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }
}
