using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Draftline.App.Services;
using Draftline.App.ViewModels;

namespace Draftline.App.Views;

public partial class ShellWindow : Window
{
    private readonly IDialogService _dialog;
    private readonly ISession _session;

    public ShellWindow(ShellViewModel vm, IDialogService dialog, ISession session)
    {
        InitializeComponent();
        DataContext = vm;
        _dialog = dialog;
        _session = session;
        dialog.ToastRequested += ShowToast;

        // 初始/重置密码后首登：强制提示改密。
        Loaded += (_, _) =>
        {
            if (_session.MustChangePassword)
            {
                _dialog.Info("当前为初始/重置密码，请先修改密码。");
                ShowChangePassword();
            }
        };
    }

    /// <summary>更改密码：弹窗校验并调后端，成功后提示。</summary>
    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        AccountToggle.IsChecked = false;
        ShowChangePassword();
    }

    private void ShowChangePassword()
    {
        var dlg = new ChangePasswordWindow { Owner = this };
        if (dlg.ShowDialog() == true)
            _dialog.Info("密码已更新。");
    }

    /// <summary>退出登录：确认后重启进程回到登录界面（会话为单例，重启最稳妥）。</summary>
    private void OnLogout(object sender, RoutedEventArgs e)
    {
        AccountToggle.IsChecked = false;
        if (!_dialog.Confirm("退出登录", "确定退出当前账号并返回登录界面？"))
            return;

        var exe = System.Environment.ProcessPath;
        if (exe is not null)
            System.Diagnostics.Process.Start(exe);
        Application.Current.Shutdown();
    }

    /// <summary>底部非阻塞 Toast：成功=绿 / 出错=红 / 普通=深色，2.8s 后淡出移除。</summary>
    private void ShowToast(ToastInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            var hex = info.Kind switch
            {
                ToastKind.Success => "#15803D",
                ToastKind.Error => "#DC2626",
                _ => "#1E2228",
            };

            var toast = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 11, 18, 11),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text = info.Message,
                    Foreground = Brushes.White,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                },
            };

            ToastHost.Children.Add(toast);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, _) => ToastHost.Children.Remove(toast);
                toast.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        });
    }
}
