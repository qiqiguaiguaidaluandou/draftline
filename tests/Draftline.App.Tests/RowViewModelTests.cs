using Draftline.App.ViewModels;
using Draftline.Core.Enums;
using Draftline.Core.Models;

namespace Draftline.App.Tests;

/// <summary>行状态自动判定：填齐必填→已处理、清空→待处理；挂起/撤销异常、已上传锁定；只读不翻转。</summary>
public class RowViewModelTests
{
    private static RowViewModel Make(bool readOnly = false)
    {
        var row = new MaterialRow
        {
            RowKey = "M-1",
            Values = new() { ["materialCode"] = "M-1", ["targetPrice"] = null },
        };
        return new RowViewModel(
            row,
            requiredKeys: new[] { "targetPrice" },
            editableKeys: new[] { "targetPrice" },
            onChanged: _ => { },
            readOnly: readOnly);
    }

    [Fact]
    public void Filling_required_marks_done_clearing_reverts_pending()
    {
        var vm = Make();
        Assert.Equal(RowStatus.Pending, vm.Status);

        vm["targetPrice"] = "88.5";
        Assert.Equal(RowStatus.Done, vm.Status);

        vm["targetPrice"] = "";
        Assert.Equal(RowStatus.Pending, vm.Status);
    }

    [Fact]
    public void Suspend_sets_exception_and_filling_does_not_flip()
    {
        var vm = Make();

        vm.Suspend("图纸缺失");
        Assert.Equal(RowStatus.Exception, vm.Status);
        Assert.True(vm.IsException);

        vm["targetPrice"] = "88.5"; // 异常态不被填值自动翻转
        Assert.Equal(RowStatus.Exception, vm.Status);
    }

    [Fact]
    public void Restore_returns_pending_when_unfilled()
    {
        var vm = Make();
        vm.Suspend("x");

        vm.Restore();
        Assert.Equal(RowStatus.Pending, vm.Status);
    }

    [Fact]
    public void Restore_returns_done_when_already_filled()
    {
        var vm = Make();
        vm.Suspend("x");
        vm["targetPrice"] = "99"; // 异常态下写入值（不翻转状态）

        vm.Restore();
        Assert.Equal(RowStatus.Done, vm.Status); // 撤销异常后若已填齐直接回已处理
    }

    [Fact]
    public void MarkUploaded_locks_status()
    {
        var vm = Make();
        vm["targetPrice"] = "1";
        vm.MarkUploaded();
        Assert.Equal(RowStatus.Uploaded, vm.Status);

        vm["targetPrice"] = ""; // 已上传不翻转
        Assert.Equal(RowStatus.Uploaded, vm.Status);
    }

    [Fact]
    public void ReadOnly_row_does_not_change_status()
    {
        var vm = Make(readOnly: true);
        vm["targetPrice"] = "88.5";
        Assert.Equal(RowStatus.Pending, vm.Status);
    }
}
