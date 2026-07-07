using Draftline.Core.Enums;

namespace Draftline.Core.Models;

/// <summary>
/// 后端下发的客户端配置（IConfigGateway）。包含时间窗规则、本地根、网关地址、保留策略等。
/// 字段集（<see cref="PricingFields"/> / <see cref="DrawingSelectionFields"/>）也由配置下发，加字段不改代码。
/// </summary>
public sealed class ClientConfig
{
    // 注意：本地数据根(LocalRoot)不在下发契约中——它是纯客户端概念，由客户端 App 启动时的
    // LocalStorageOptions(我的文档\Draftline_Data)自行决定，后端无从、也不应替客户端指定本机路径。

    /// <summary>后端无状态网关地址（取数 / 回传 / 认证）。</summary>
    public string GatewayBaseUrl { get; init; } = "http://localhost:8080";

    /// <summary>核价时间窗（2窗）。</summary>
    public IReadOnlyList<CollectionWindow> PricingWindows { get; init; } = Array.Empty<CollectionWindow>();

    /// <summary>挑图时间窗（3窗，独立）。</summary>
    public IReadOnlyList<CollectionWindow> DrawingSelectionWindows { get; init; } = Array.Empty<CollectionWindow>();

    /// <summary>核价表单字段集。</summary>
    public IReadOnlyList<FieldDefinition> PricingFields { get; init; } = Array.Empty<FieldDefinition>();

    /// <summary>挑图表单字段集。</summary>
    public IReadOnlyList<FieldDefinition> DrawingSelectionFields { get; init; } = Array.Empty<FieldDefinition>();

    /// <summary>已处理批次本地保留天数（超期可清理）。0 = 不自动清理。</summary>
    public int RetentionDaysForDone { get; init; } = 30;

    public IReadOnlyList<CollectionWindow> WindowsFor(FlowType flow) =>
        flow == FlowType.Pricing ? PricingWindows : DrawingSelectionWindows;

    public IReadOnlyList<FieldDefinition> FieldsFor(FlowType flow) =>
        flow == FlowType.Pricing ? PricingFields : DrawingSelectionFields;
}
