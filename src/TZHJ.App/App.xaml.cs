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
            // 离线模式：全用 Mock 造数，无需后端。下方 UI/VM 与此切换无关。
            services.AddTzhjMockInfrastructure(mock);
        }
        else
        {
            // 真 HTTP 链路：连后端 TZHJ.Gateway（取数/回传/认证/配置）。UI/VM 不动。
            var http = new HttpOptions();
            config.GetSection("Http").Bind(http);
            services.AddTzhjHttpInfrastructure(http);
        }

        // 应用层服务
        services.AddSingleton<ISession, Session>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IExplorerService, ExplorerService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<SessionSyncScheduler>();

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

            // 登录后启动会话内取数调度：立即登录补拉 + 每 120s 会话内定时触发（不卡界面）。
            Services.GetRequiredService<SessionSyncScheduler>().Start();
        }
        else
        {
            Shutdown();
        }
    }
}
