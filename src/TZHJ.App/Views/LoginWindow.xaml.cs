using System.Windows;
using System.Windows.Controls;
using TZHJ.App.ViewModels;

namespace TZHJ.App.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.LoginSucceeded += (_, _) => { DialogResult = true; Close(); };
    }

    // PasswordBox.Password 不可直接绑定，转交 VM。
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _vm.Password = box.Password;
    }
}
