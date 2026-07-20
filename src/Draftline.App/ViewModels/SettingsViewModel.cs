using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Draftline.App.Services;
using Draftline.Infrastructure.Options;
using Draftline.Infrastructure.Storage;

namespace Draftline.App.ViewModels;

/// <summary>
/// 设置：当前操作员（本机固定）、本地数据根、客户端版本。图纸由操作员到本地文件夹自行打开。
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IExplorerService _explorer;
    private readonly IDialogService _dialog;
    private readonly IUpdateService _update;
    private readonly IClientSettingsStore _settings;

    public SettingsViewModel(ISession session, IExplorerService explorer, IDialogService dialog, IUpdateService update, LocalStorageOptions storage, IClientSettingsStore settings)
    {
        _explorer = explorer;
        _dialog = dialog;
        _update = update;
        _settings = settings;

        Title = "设置";
        var op = session.Operator;
        OperatorText = $"{op.DisplayName} · 工号 {op.EmployeeId} · {op.Department} / {op.Position}";
        LocalRoot = storage.Root;
        // 客户端版本显示当前 ClickOnce 部署版本（重登/更新后自动反映），非部署运行时回退程序集版本。
        Version = _update.GetStatus().CurrentVersion?.ToString()
                  ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                  ?? "0.1.0";
    }

    public string OperatorText { get; }
    public string LocalRoot { get; }
    public string Version { get; }

    [RelayCommand]
    private void OpenRoot() => _explorer.OpenFolder(LocalRoot);

    /// <summary>
    /// 更改本地数据根：选父目录 → 确认 → 记下"新根 + 待迁移旧根" → 重启后由启动流程完成迁移并生效。
    /// 迁移放到重启后做，避开当前会话的同步/文件句柄，最安全。
    /// </summary>
    [RelayCommand]
    private void ChangeRoot()
    {
        // 定位到当前根的上一级，方便用户就近选。
        var picked = _explorer.PickFolder(System.IO.Path.GetDirectoryName(LocalRoot));
        if (picked is null) return;

        var newRoot = LocalRootResolver.RootUnder(picked);
        if (string.Equals(
                System.IO.Path.GetFullPath(newRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                System.IO.Path.GetFullPath(LocalRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                System.StringComparison.OrdinalIgnoreCase))
        {
            _dialog.Info("所选位置与当前一致，未更改。");
            return;
        }

        if (!_dialog.Confirm("更改数据存放位置",
                $"数据根目录将更改为：\n{newRoot}\n\n现有数据会迁移到新位置，程序需要重启后生效。是否继续？"))
            return;

        var s = _settings.Load();
        s.PendingMoveFrom = LocalRoot;   // 旧根待迁移
        s.LocalRoot = newRoot;           // 新根从下次启动起生效
        _settings.Save(s);

        if (_dialog.Confirm("需要重启", "设置已保存。是否立即重启程序以完成迁移并生效？"))
            App.Restart();
    }

    [RelayCommand]
    private void CheckUpdate()
    {
        var s = _update.GetStatus();
        var current = s.CurrentVersion?.ToString() ?? Version;

        // 开发 / 独立运行（未经 ClickOnce 部署）：无更新通道。
        if (!s.IsDeployed)
        {
            _dialog.Info("当前为开发 / 独立运行版本，未经 ClickOnce 部署，无法检查更新。");
            return;
        }

        // 本次启动刚由前台更新装上新版：直接告知结果。
        if (s.UpdatedVersion is { } updated && updated != s.CurrentVersion)
        {
            _dialog.Success($"已更新至 v{updated}。");
            return;
        }

        // 更新在每次启动时自动检查并应用（Foreground）；此处通过重启触发一次检查。
        if (!s.CanRestart)
        {
            _dialog.Info($"当前版本 v{current}。更新在程序启动时自动检查并应用，请手动重启程序以获取最新版本。");
            return;
        }

        if (_dialog.Confirm("检查更新",
                $"当前版本 v{current}。\n更新在程序启动时自动检查并应用。\n是否立即重启以获取最新版本？"))
        {
            _update.RestartForUpdate();
        }
    }
}
