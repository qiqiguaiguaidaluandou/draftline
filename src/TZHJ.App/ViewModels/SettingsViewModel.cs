using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using TZHJ.App.Services;

namespace TZHJ.App.ViewModels;

/// <summary>
/// 设置：当前操作员（本机固定）、本地数据根、后端网关地址、客户端版本。图纸由操作员到本地文件夹自行打开。
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IExplorerService _explorer;
    private readonly IDialogService _dialog;

    public SettingsViewModel(ISession session, IExplorerService explorer, IDialogService dialog)
    {
        _explorer = explorer;
        _dialog = dialog;

        Title = "设置";
        var op = session.Operator;
        OperatorText = $"{op.DisplayName} · 工号 {op.EmployeeId} · {op.Department} / {op.Position}";
        LocalRoot = session.Config.LocalRoot;
        GatewayUrl = session.Config.GatewayBaseUrl;
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
    }

    public string OperatorText { get; }
    public string LocalRoot { get; }
    public string GatewayUrl { get; }
    public string Version { get; }

    [RelayCommand]
    private void OpenRoot() => _explorer.OpenFolder(LocalRoot);

    [RelayCommand]
    private void CheckUpdate() => _dialog.Info("ClickOnce 检查更新（骨架占位，发布后启用）。");
}
