using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TZHJ.App.Services;
using TZHJ.App.ViewModels;

namespace TZHJ.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow(ShellViewModel vm, IDialogService dialog)
    {
        InitializeComponent();
        DataContext = vm;
        dialog.ToastRequested += ShowToast;
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
