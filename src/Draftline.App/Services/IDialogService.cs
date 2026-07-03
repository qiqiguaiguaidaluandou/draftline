using System.Windows;
using Draftline.App.Views;

namespace Draftline.App.Services;

public enum ToastKind { Info, Success, Error }

public sealed record ToastInfo(string Message, ToastKind Kind);

/// <summary>
/// 对话/反馈服务。需要决策或输入的（确认、文本输入）走模态；纯反馈（暂存/上传/补拉结果）走底部 Toast。
/// </summary>
public interface IDialogService
{
    /// <summary>需要用户决策，模态。</summary>
    bool Confirm(string title, string message);

    /// <summary>文本输入（如填异常原因），模态。取消返回 null。</summary>
    string? Prompt(string title, string message, string? defaultValue = null);

    /// <summary>普通反馈（Toast）。</summary>
    void Info(string message);

    /// <summary>成功反馈（绿色 Toast）。</summary>
    void Success(string message);

    /// <summary>错误反馈（红色 Toast）。</summary>
    void Error(string message);

    /// <summary>由壳窗口订阅以渲染 Toast；未订阅时降级为 MessageBox。</summary>
    event Action<ToastInfo>? ToastRequested;
}

public sealed class DialogService : IDialogService
{
    public event Action<ToastInfo>? ToastRequested;

    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var dlg = new ConfirmWindow(title, message) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    public void Info(string message) => Toast(new ToastInfo(message, ToastKind.Info));
    public void Success(string message) => Toast(new ToastInfo(message, ToastKind.Success));
    public void Error(string message) => Toast(new ToastInfo(message, ToastKind.Error));

    private void Toast(ToastInfo info)
    {
        if (ToastRequested is { } handler)
            handler(info);
        else // 还没有壳窗口（如登录阶段）时降级
            MessageBox.Show(info.Message, info.Kind == ToastKind.Error ? "出错" : "提示");
    }

    public string? Prompt(string title, string message, string? defaultValue = null)
    {
        // 代码构建的简单输入框（避免额外 XAML）。
        var window = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
            ResizeMode = ResizeMode.NoResize,
        };

        var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new System.Windows.Controls.TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 8) });
        var input = new System.Windows.Controls.TextBox { Text = defaultValue ?? string.Empty, MinWidth = 320 };
        root.Children.Add(input);

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new System.Windows.Controls.Button { Content = "确定", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 72, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = input.Text; window.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        window.Content = root;
        input.Focus();
        input.SelectAll();
        return window.ShowDialog() == true ? result : null;
    }
}
