namespace TZHJ.Core.Contracts;

/// <summary>
/// 取数网关：带工号调 EBS 取本人需求，再按物料编码调 PLM 取图纸 + "是否存在变更"，
/// 组织成一个批次返回。客户端拿到后落本地（见 ILocalBatchStore）。真接口到位前 Mock 造数。
/// </summary>
public interface IDataGateway
{
    Task<FetchResult> FetchBatchAsync(FetchRequest request, CancellationToken ct = default);
}
