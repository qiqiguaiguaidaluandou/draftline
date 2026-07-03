using Draftline.Core.Models;

namespace Draftline.Gateway.Stores;

/// <summary>配置下发存储：按工号给出 ClientConfig（时间窗/字段集/本地根/保留天数）。
/// 骨架用内存实现（从 Draftline.Core 默认 schema 种子）；上线落 PostgreSQL config 表。</summary>
public interface IConfigStore
{
    ClientConfig Get(string employeeId);
}
