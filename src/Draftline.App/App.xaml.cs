using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Draftline.App.Services;
using Draftline.App.ViewModels;
using Draftline.App.Views;
using Draftline.Infrastructure;
using Draftline.Infrastructure.Options;

namespace Draftline.App;

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
            .AddJsonFile("appsettings.local.json", optional: true)
            .Build();

        // ---------- DI ----------
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);

        // 真 HTTP 链路：连后端 Draftline.Gateway（取数/回传/认证/配置/操作日志）。客户端唯一链路。
        var http = new HttpOptions();
        config.GetSection("Http").Bind(http);

        // 强制标准化：数据文件夹固定在"我的文档\data"，不再受配置文件影响
        http.LocalRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "data");

        services.AddDraftlineHttpInfrastructure(http);

        // 应用层服务
        services.AddSingleton<ISession, Session>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IExplorerService, ExplorerService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<SessionSyncScheduler>();

        // ViewModel / 窗口（BatchList/Work/Exception/Settings 由 NavigationService 用
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

            // ClickOnce：安装 / 更新后的首次运行，提示当前版本（仅经部署运行时；壳窗口已订阅 Toast）。
            var update = Services.GetRequiredService<IUpdateService>().GetStatus();
            if (update is { IsDeployed: true, IsFirstRun: true, CurrentVersion: { } v })
                Services.GetRequiredService<IDialogService>().Success($"客户端已更新至 v{v}。");
        }
        else
        {
            Shutdown();
        }
    }
}
