using Microsoft.Extensions.DependencyInjection;
using TZHJ.Core.Contracts;
using TZHJ.Infrastructure.Gateways.Mock;
using TZHJ.Infrastructure.Options;
using TZHJ.Infrastructure.Storage;

namespace TZHJ.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册 Mock 网关 + 本地存储。这是"无接口先开发"的总开关：
    /// 真接口到位后，把下面四个网关换成 Http* 实现（或新增 AddTzhjHttpInfrastructure），存储部分不动。
    /// </summary>
    public static IServiceCollection AddTzhjMockInfrastructure(this IServiceCollection services, MockOptions mock)
    {
        services.AddSingleton(mock);
        services.AddSingleton(new LocalStorageOptions { Root = mock.LocalRoot });

        // 字段提供者：默认 schema，登录后可被下发配置覆盖（DefaultFieldProvider.Apply）。
        services.AddSingleton<DefaultFieldProvider>();
        services.AddSingleton<IFieldProvider>(sp => sp.GetRequiredService<DefaultFieldProvider>());

        // 三个对外边界 + 配置下发：当前全为 Mock。
        services.AddSingleton<IAuthGateway, MockAuthGateway>();
        services.AddSingleton<IConfigGateway, MockConfigGateway>();
        services.AddSingleton<IDataGateway, MockDataGateway>();
        services.AddSingleton<ISubmitGateway, MockSubmitGateway>();

        // 本地存储（非网关，真接口上线后不变）。
        services.AddSingleton<ILocalBatchStore, LocalBatchStore>();

        return services;
    }
}
