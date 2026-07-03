using System.Text.Json;
using System.Text.Json.Serialization;

namespace Draftline.Infrastructure.Gateways.Http;

/// <summary>
/// 客户端与后端共用的 JSON 约定：camelCase 属性名 + 枚举走字符串（FlowType=Pricing/DrawingSelection）。
/// 必须与后端 Program.cs 的 ConfigureHttpJsonOptions 保持一致，否则两阶段取数/回传会对不上。
/// 注意：字典 key（行值字典）不套命名策略，原样保留——与后端默认行为一致。
/// </summary>
internal static class HttpJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        // Web 默认 = camelCase 属性名 + 大小写不敏感读 + 数字可从字符串读，与 ASP.NET Core 一致。
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
