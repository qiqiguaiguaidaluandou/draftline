using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Draftline.App.Services;
using Draftline.Core.Contracts;
using Draftline.Infrastructure.Fields;
using Draftline.Infrastructure.Options;

namespace Draftline.App.ViewModels;

/// <summary>
/// 登录：调 IAuthGateway 认证 → 调 IConfigGateway 取配置 → 应用字段集/本地根 → 写会话。
/// 成功后触发 <see cref="LoginSucceeded"/>，由 LoginWindow 切到主界面。
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthGateway _auth;
    private readonly IConfigGateway _config;
    private readonly ISession _session;
    private readonly DefaultFieldProvider _fieldProvider;
    private readonly LocalStorageOptions _storage;

    public LoginViewModel(
        IAuthGateway auth,
        IConfigGateway config,
        ISession session,
        DefaultFieldProvider fieldProvider,
        LocalStorageOptions storage)
    {
        _auth = auth;
        _config = config;
        _session = session;
        _fieldProvider = fieldProvider;
        _storage = storage;
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

            // 取下发配置，应用字段集。
            // 本地数据根必须由客户端决定：后端跑在 Linux 服务器上，其下发的 LocalRoot
            // 会是 /home/xxx/文档/… ，对 Windows 客户端无意义、无法在资源管理器打开。
            // 用客户端启动时算好的 _storage.Root（我的文档\data）覆盖下发值，保证 Batch.FolderPath、
            // LocationRootPath、PoolPath 等全部基于本机真实路径。
            var config = await _config.GetConfigAsync(auth.Operator.EmployeeId);
            config.LocalRoot = _storage.Root;
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
