using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.Tests.Storage;

/// <summary>批次键 = 流程 + 窗口起止；目录名格式化/解析往返（防重复的基础）。</summary>
public class BatchKeyTests
{
    [Fact]
    public void FolderName_roundtrips_same_day_window()
    {
        var start = new DateTime(2026, 5, 27, 9, 31, 0);
        var end = new DateTime(2026, 5, 27, 15, 30, 0);

        var name = LocalPaths.BatchFolderName(start, end);
        Assert.Equal("20260527_0931-1530", name); // 同日：末段省略日期

        Assert.True(LocalPaths.TryParseFolderName(name, out var s, out var e));
        Assert.Equal(start, s);
        Assert.Equal(end, e);
    }

    [Fact]
    public void FolderName_roundtrips_cross_day_window()
    {
        var start = new DateTime(2026, 5, 26, 15, 31, 0);
        var end = new DateTime(2026, 5, 27, 9, 30, 0);

        var name = LocalPaths.BatchFolderName(start, end);
        Assert.Equal("20260526_1531-20260527_0930", name);

        Assert.True(LocalPaths.TryParseFolderName(name, out var s, out var e));
        Assert.Equal(start, s);
        Assert.Equal(end, e);
    }

    [Theory]
    [InlineData("not-a-window")]
    [InlineData("nodash")]
    [InlineData("")]
    public void TryParse_rejects_garbage(string folderName)
    {
        Assert.False(LocalPaths.TryParseFolderName(folderName, out _, out _));
    }

    [Fact]
    public void BatchKey_is_folder_name()
    {
        var batch = new Batch
        {
            Flow = FlowType.Pricing,
            EmployeeId = "10086",
            WindowStart = new DateTime(2026, 5, 26, 15, 31, 0),
            WindowEnd = new DateTime(2026, 5, 27, 9, 30, 0),
            FolderName = "20260526_1531-20260527_0930",
            FolderPath = "x",
            Location = BatchLocation.Todo,
        };
        Assert.Equal(batch.FolderName, batch.Key);
    }
}
