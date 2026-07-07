using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Draftline.App.Services;
using Draftline.Core.Contracts;
using Draftline.Infrastructure.Fields;

namespace Draftline.App.ViewModels;

/// <summary>
/// 登录：调 IAuthGateway 认证 → 调 IConfigGateway 取配置 → 应用字段集 → 写会话。
/// 成功后触发 <see cref="LoginSucceeded"/>，由 LoginWindow 切到主界面。
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthGateway _auth;
    private readonly IConfigGateway _config;
    private readonly ISession _session;
    private readonly DefaultFieldProvider _fieldProvider;

    public LoginViewModel(
        IAuthGateway auth,
        IConfigGateway config,
        ISession session,
        DefaultFieldProvider fieldProvider)
    {
        _auth = auth;
        _config = config;
        _session = session;
        _fieldProvider = fieldProvider;
    }

    [ObservableProperty] private string _employeeId = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public event EventHandler? LoginSucceeded;

    [RelayCommand]
    private async Task LoginAsync()
    {
        Error = null;

        var empId = EmployeeId.Trim();
        if (empId.Length == 0 || Password.Length == 0)
        {
            Error = "请输入工号和密码。";
            return;
        }

        IsBusy = true;
        try
        {
            var auth = await _auth.LoginAsync(empId, Password);
            if (!auth.Success || auth.Operator is null)
            {
                Error = auth.Message ?? "登录失败。";
                return;
            }

            // 取下发配置，应用字段集。本地数据根不在下发内容里——它是纯客户端概念，
            // 由 App 启动时的 LocalStorageOptions（我的文档\Draftline_Data）统一决定。
            var config = await _config.GetConfigAsync(auth.Operator.EmployeeId);
            _fieldProvider.Apply(config);
            _session.SignIn(auth.Operator, config, auth.MustChangePassword);

            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Error = FriendlyError.Describe(ex, "登录");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
