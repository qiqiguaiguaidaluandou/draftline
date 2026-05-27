using TZHJ.Core.Models;
using TZHJ.Core.Schemas;

namespace TZHJ.Gateway.Stores;

/// <summary>配置存储内存实现：把今天 MockConfigGateway 的默认值挪到后端。
/// LocalRoot / GatewayBaseUrl 来自网关 appsettings "Config" 节（骨架阶段全员一致）。</summary>
public sealed class InMemoryConfigStore : IConfigStore
{
    private readonly ConfigStoreOptions _options;

    public InMemoryConfigStore(ConfigStoreOptions options) => _options = options;

    public ClientConfig Get(string employeeId) => new()
    {
        LocalRoot = _options.LocalRoot,
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
    /// <summary>客户端本地数据根目录（骨架阶段由后端下发；机器相关，上线再细化按机/按人）。</summary>
    public string LocalRoot { get; set; } = "TZHJ_Data";

    /// <summary>下发给客户端的网关地址（骨架阶段客户端实际用自己 appsettings 的 Http:BaseUrl，此处仅填充）。</summary>
    public string GatewayBaseUrl { get; set; } = "http://localhost:8080";

    public int RetentionDaysForDone { get; set; } = 30;
}
