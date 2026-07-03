using System.Net;
using System.Net.Http;
using Draftline.App.Services;

namespace Draftline.App.Tests;

/// <summary>断网/超时/令牌失效/服务器报错应翻成不同的操作员提示；未知异常退回原消息。带动作前缀。</summary>
public class FriendlyErrorTests
{
    [Fact]
    public void Timeout_is_phrased_as_timeout()
    {
        var msg = FriendlyError.Describe(new TaskCanceledException(), "回传");
        Assert.StartsWith("回传失败：", msg);
        Assert.Contains("超时", msg);
    }

    [Fact]
    public void Connection_failure_no_status_is_phrased_as_offline()
    {
        // StatusCode 为空 = 连接层失败（断网/拒绝/DNS）。
        var msg = FriendlyError.Describe(new HttpRequestException("Connection refused"), "补拉");
        Assert.Contains("无法连接服务器", msg);
    }

    [Fact]
    public void Unauthorized_guides_relogin()
    {
        var ex = new HttpRequestException("401", null, HttpStatusCode.Unauthorized);
        var msg = FriendlyError.Describe(ex, "登录");
        Assert.Contains("重新登录", msg);
    }

    [Fact]
    public void Other_status_code_reports_the_code()
    {
        var ex = new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
        var msg = FriendlyError.Describe(ex, "回传");
        Assert.Contains("500", msg);
    }

    [Fact]
    public void Unknown_exception_falls_back_to_message()
    {
        var msg = FriendlyError.Describe(new InvalidOperationException("磁盘已满"), "暂存");
        Assert.Contains("磁盘已满", msg);
    }
}
