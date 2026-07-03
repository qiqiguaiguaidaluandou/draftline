using System.Windows;

namespace Draftline.App.Views;

/// <summary>themed 确认对话框，替代原生 MessageBox（供 DialogService.Confirm 使用）。</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
