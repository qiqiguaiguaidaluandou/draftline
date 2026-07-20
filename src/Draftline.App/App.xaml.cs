using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Draftline.App.Services;
using Draftline.App.ViewModels;
using Draftline.App.Views;
using Draftline.Infrastructure;
using Draftline.Infrastructure.Options;
using Draftline.Infrastructure.Storage;

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

        // 本地数据根：优先用户在本机选定的路径（%AppData%\Draftline\client-settings.json），
        // 未自定义则默认"我的文档\Draftline_Data"。这是本地数据根的唯一真源，
        // 登录后由 LoginViewModel 用它覆盖后端下发的 LocalRoot（后端不指定本机物理路径）。
        var settingsStore = new ClientSettingsStore();
        http.LocalRoot = ResolveLocalRoot(settingsStore);

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

    /// <summary>
    /// 解析本机数据根：首次运行让用户选位置；有待迁移则在此把旧根整体迁到新根。
    /// 均在 DI/登录之前完成——早于任何组件用到路径，迁移不碰运行中的文件句柄。
    /// </summary>
    private static string ResolveLocalRoot(IClientSettingsStore store)
    {
        var firstRun = !store.Exists;
        var settings = store.Load();

        // 首次运行：询问是否自定义位置；无论选与不选都落盘，避免每次都问。
        if (firstRun)
        {
            var msg = $"文件数据默认存放在“{LocalRootResolver.DefaultRoot()}”（通常在 C 盘）。\n\n是否改为自定义位置？";
            if (MessageBox.Show(msg, "选择数据存放位置", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择数据存放位置（将在其下创建 Draftline_Data 文件夹）" };
                if (dlg.ShowDialog() == true)
                    settings.LocalRoot = LocalRootResolver.RootUnder(dlg.FolderName);
            }
            store.Save(settings);
        }

        var root = string.IsNullOrWhiteSpace(settings.LocalRoot) ? LocalRootResolver.DefaultRoot() : settings.LocalRoot;

        // 待迁移：把旧根整体迁到当前根。失败则退回旧根继续用（不丢数据、不把用户困在空目录）。
        if (!string.IsNullOrWhiteSpace(settings.PendingMoveFrom))
        {
            try
            {
                DirectoryMigrator.Move(settings.PendingMoveFrom!, root);
                settings.PendingMoveFrom = null;
                store.Save(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"数据从“{settings.PendingMoveFrom}”迁移到“{root}”失败，将继续使用原位置。\n\n{ex.Message}",
                    "迁移失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                root = settings.PendingMoveFrom!;
                settings.LocalRoot = settings.PendingMoveFrom;
                settings.PendingMoveFrom = null;
                store.Save(settings);
            }
        }

        return root;
    }

    /// <summary>重启本进程（更改数据根后生效用）：拉起新实例再退出当前。</summary>
    public static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
            System.Diagnostics.Process.Start(exe);
        Current.Shutdown();
    }
}
