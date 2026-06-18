using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using Xunit;

namespace TZHJ.Tests.Organization;

public class LocalPathsTests
{
    private const string Root = @"C:\Data";

    [Fact]
    public void LocalBatchDir_ShouldFollowFiveLevelHierarchy()
    {
        // Root / Flow / Status / Group / Batch
        var path = LocalPaths.LocalBatchDir(Root, FlowType.Pricing, BatchLocation.Todo, "核价一组", "20260616_0900");
        
        // Use Path.Combine logic to be OS-agnostic for the test itself if needed, 
        // but here we check the order.
        Assert.Contains("核价", path);
        Assert.Contains("待处理", path);
        Assert.Contains("核价一组", path);
        Assert.Contains("20260616_0900", path);
        
        // Verify specifically the order
        var parts = path.Split(Path.DirectorySeparatorChar);
        var lastFour = parts.TakeLast(4).ToList();
        Assert.Equal("图纸核价", lastFour[0]);
        Assert.Equal("待处理", lastFour[1]);
        Assert.Equal("核价一组", lastFour[2]);
        Assert.Equal("20260616_0900", lastFour[3]);
    }

    [Fact]
    public void ServerBatchDir_ShouldBeFlat()
    {
        // Root / Flow / Group / Batch
        var path = LocalPaths.ServerBatchDir(Root, FlowType.Pricing, "核价一组", "20260616_0900");
        
        var parts = path.Split(Path.DirectorySeparatorChar);
        var lastThree = parts.TakeLast(3).ToList();
        Assert.Equal("图纸核价", lastThree[0]);
        Assert.Equal("核价一组", lastThree[1]);
        Assert.Equal("20260616_0900", lastThree[2]);
    }

    [Fact]
    public void DrawingSelection_ShouldHaveFlattenedHierarchy()
    {
        // Local: Root / Flow / Status / Batch
        var localPath = LocalPaths.LocalBatchDir(Root, FlowType.DrawingSelection, BatchLocation.Todo, "IgnoreGroup", "20260616_0900");
        var localParts = localPath.Split(Path.DirectorySeparatorChar);
        var localLastThree = localParts.TakeLast(3).ToList();
        Assert.Equal("机加中心挑图", localLastThree[0]);
        Assert.Equal("待处理", localLastThree[1]);
        Assert.Equal("20260616_0900", localLastThree[2]);

        // Server: Root / Flow / Batch
        var serverPath = LocalPaths.ServerBatchDir(Root, FlowType.DrawingSelection, "IgnoreGroup", "20260616_0900");
        var serverParts = serverPath.Split(Path.DirectorySeparatorChar);
        var serverLastTwo = serverParts.TakeLast(2).ToList();
        Assert.Equal("机加中心挑图", serverLastTwo[0]);
        Assert.Equal("20260616_0900", serverLastTwo[1]);
    }
}
