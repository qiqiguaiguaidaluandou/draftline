using System.Windows;

namespace TZHJ.App.Views;

/// <summary>
/// 更改密码对话框（占位）：当前认证为 Mock、改密码由 DHR 统一管理，
/// 这里仅做必填/一致性校验，确定后由调用方提示"接口接入后生效"。
/// </summary>
public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => OldBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (OldBox.Password.Length == 0 || NewBox.Password.Length == 0)
        {
            ShowError("请填写当前密码和新密码。");
            return;
        }
        if (NewBox.Password != ConfirmBox.Password)
        {
            ShowError("两次输入的新密码不一致。");
            return;
        }
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
