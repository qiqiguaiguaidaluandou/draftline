using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Draftline.App.Services;
using Draftline.Core.Contracts;

namespace Draftline.App.Views;

/// <summary>
/// 更改密码：校验后调后端 /api/auth/change-password（凭当前会话令牌，工号以令牌为准）。
/// 成功后清除会话的强制改密标志。
/// </summary>
public partial class ChangePasswordWindow : Window
{
    private const int MinLength = 8;

    private readonly IAuthGateway _auth;
    private readonly ISession _session;

    public ChangePasswordWindow()
    {
        InitializeComponent();
        _auth = App.Services.GetRequiredService<IAuthGateway>();
        _session = App.Services.GetRequiredService<ISession>();
        Loaded += (_, _) => OldBox.Focus();
    }

    private async void OnConfirm(object sender, RoutedEventArgs e)
    {
        HideError();

        if (OldBox.Password.Length == 0 || NewBox.Password.Length == 0)
        {
            ShowError("请填写当前密码和新密码。");
            return;
        }
        if (NewBox.Password.Length < MinLength)
        {
            ShowError($"新密码长度至少 {MinLength} 位。");
            return;
        }
        if (NewBox.Password != ConfirmBox.Password)
        {
            ShowError("两次输入的新密码不一致。");
            return;
        }
        if (NewBox.Password == OldBox.Password)
        {
            ShowError("新密码不能与当前密码相同。");
            return;
        }

        ConfirmButton.IsEnabled = false;
        try
        {
            var result = await _auth.ChangePasswordAsync(OldBox.Password, NewBox.Password);
            if (!result.Success)
            {
                ShowError(result.Message ?? "改密失败。");
                return;
            }

            _session.MustChangePassword = false;
            DialogResult = true;
        }
        catch (System.Exception ex)
        {
            ShowError(FriendlyError.Describe(ex, "改密"));
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
