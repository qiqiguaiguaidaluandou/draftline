using System.Diagnostics;
using System.Windows;

namespace TZHJ.App.Services;

/// <summary>
/// 更新检查状态（只读快照，不联网）。
/// </summary>
/// <param name="IsDeployed">是否经 ClickOnce 部署运行；开发 / 直接跑 exe 时为 false。</param>
/// <param name="CurrentVersion">当前 ClickOnce 部署版本（非程序集版本）。</param>
/// <param name="UpdatedVersion">本次启动由前台更新装上的新版本；无更新时通常为空或同当前版本。</param>
/// <param name="IsFirstRun">是否安装 / 更新后的首次运行。</param>
/// <param name="CanRestart">是否存在可用于重新激活的地址（据此触发前台更新）。</param>
public sealed record UpdateStatus(
    bool IsDeployed,
    Version? CurrentVersion,
    Version? UpdatedVersion,
    bool IsFirstRun,
    bool CanRestart);

/// <summary>
/// ClickOnce 更新服务。
///
/// 背景：.NET 8 已移除 System.Deployment.Application 的方法（CheckForUpdate / Update），
/// 无法在程序内主动拉取并安装更新。自动更新实际走 UpdateMode=Foreground（见
/// Properties/PublishProfiles/ClickOnceProfile.pubxml）——管理员发布新版后，客户端下次启动即
/// 同步检查并自动应用。启动器仅通过 ClickOnce_* 环境变量把只读属性共享给应用。
///
/// 本服务据此：① 读取部署状态 / 版本（GetStatus）；② 通过重新激活来触发一次前台更新
/// （RestartForUpdate）。参考：
/// https://learn.microsoft.com/visualstudio/deployment/access-clickonce-deployment-properties-dotnet
/// </summary>
public interface IUpdateService
{
    /// <summary>读取当前 ClickOnce 部署状态（只读，不联网）。</summary>
    UpdateStatus GetStatus();

    /// <summary>通过重新激活触发前台更新：启动激活地址后关闭当前实例。仅在 CanRestart 时有效。</summary>
    void RestartForUpdate();
}

public sealed class UpdateService : IUpdateService
{
    public UpdateStatus GetStatus() => new(
        IsDeployed: IsNetworkDeployed,
        CurrentVersion: ParseVersion(Get("ClickOnce_CurrentVersion")),
        UpdatedVersion: ParseVersion(Get("ClickOnce_UpdatedVersion")),
        IsFirstRun: Equals(Get("ClickOnce_IsFirstRun"), "true"),
        CanRestart: ReactivationUri is not null);

    public void RestartForUpdate()
    {
        if (ReactivationUri is not { } uri) return;

        // 启动 ClickOnce 部署清单（.application）地址 → 交由启动器做前台更新并重新拉起应用，
        // 随后关闭当前实例。安装型部署下这是触发一次更新检查的标准做法。
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        Application.Current?.Shutdown();
    }

    // 优先用更新源（部署清单位置），回退到激活地址。
    private static Uri? ReactivationUri =>
        ParseUri(Get("ClickOnce_UpdateLocation")) ?? ParseUri(Get("ClickOnce_ActivationUri"));

    private static bool IsNetworkDeployed => Equals(Get("ClickOnce_IsNetworkDeployed"), "true");

    private static string? Get(string name) => Environment.GetEnvironmentVariable(name);
    private static bool Equals(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    private static Version? ParseVersion(string? s) => Version.TryParse(s, out var v) ? v : null;
    private static Uri? ParseUri(string? s) => Uri.TryCreate(s, UriKind.Absolute, out var u) ? u : null;
}
