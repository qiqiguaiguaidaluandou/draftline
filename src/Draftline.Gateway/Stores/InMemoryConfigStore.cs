using System.IO;
using Draftline.Core.Models;
using Draftline.Core.Schemas;

namespace Draftline.Gateway.Stores;

/// <summary>配置存储内存实现：把今天 MockConfigGateway 的默认值挪到后端。
/// GatewayBaseUrl 来自网关 appsettings "Config" 节（骨架阶段全员一致）。</summary>
public sealed class InMemoryConfigStore : IConfigStore
{
    private readonly ConfigStoreOptions _options;

    public InMemoryConfigStore(ConfigStoreOptions options) => _options = options;

    public ClientConfig Get(string employeeId) => new()
    {
        // 强制标准化：下发给客户端的路径固定为"我的文档\Draftline_Data"。
        // 注意：由于后端运行在 Linux 服务器上，此处的 Environment 路径逻辑主要确保契约一致性，
        // 客户端在接收到 ClientConfig 后若仍需覆盖，以客户端 App.xaml.cs 逻辑为准。
        LocalRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Draftline_Data"),
        GatewayBaseUrl = _options.GatewayBaseUrl,
        PricingWindows = CollectionSchedules.Pricing,
        DrawingSelectionWindows = CollectionSchedules.DrawingSelection,
        PricingFields = FieldSchemas.Pricing,
        DrawingSelectionFields = FieldSchemas.DrawingSelection,
        RetentionDaysForDone = _options.RetentionDaysForDone,
    };
}

/// <summary>配置下发的机器相关项（来自 appsettings "Config" 节）。</summary>
public sealed class ConfigStoreOptions
{
    /// <summary>下发给客户端的网关地址（骨架阶段客户端实际用自己 appsettings 的 Http:BaseUrl，此处仅填充）。</summary>
    public string GatewayBaseUrl { get; set; } = "http://localhost:8080";

    public int RetentionDaysForDone { get; set; } = 30;
}
