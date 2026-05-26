using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TZHJ.App.Services;
using TZHJ.App.ViewModels;
using TZHJ.App.Views;
using TZHJ.Infrastructure;
using TZHJ.Infrastructure.Options;

namespace TZHJ.App;

public partial class App : Application
{
    /// <summary>全局服务容器。</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---------- 配置 ----------
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var mock = new MockOptions();
        config.GetSection("Mock").Bind(mock);
        var useMock = config.GetValue("UseMock", true);

        // ---------- DI ----------
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);

        if (useMock)
        {
            // 无接口先开发：全用 Mock。真接口到位后换成 AddTzhjHttpInfrastructure(...) 即可，下方 UI/VM 不动。
            services.AddTzhjMockInfrastructure(mock);
        }
        else
        {
            throw new NotSupportedException("真实网关尚未接入；当前请设 appsettings.json 的 UseMock=true。");
        }

        // 应用层服务
        services.AddSingleton<ISession, Session>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IExplorerService, ExplorerService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModel / 窗口（BatchList/Work/Exception/Schedule/Settings 由 NavigationService 用
        // ActivatorUtilities 按需创建，无需在此注册）
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<ShellWindow>();

        Services = services.BuildServiceProvider();

        // ---------- 登录 → 主界面 ----------
        var login = Services.GetRequiredService<LoginWindow>();
        if (login.ShowDialog() == true)
        {
            var shell = Services.GetRequiredService<ShellWindow>();
            MainWindow = shell;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            shell.Show();
        }
        else
        {
            Shutdown();
        }
    }
}
