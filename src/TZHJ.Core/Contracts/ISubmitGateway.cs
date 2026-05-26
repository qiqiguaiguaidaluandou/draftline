namespace TZHJ.Core.Contracts;

/// <summary>
/// 回传网关：整批正常行经后端回传到目标系统（核价→SRM / 挑图→EBS），后端记审计日志。
/// 挂起异常行不在请求内。真接口到位前 Mock 模拟成功/失败。
/// </summary>
public interface ISubmitGateway
{
    Task<SubmitResult> SubmitBatchAsync(SubmitRequest request, CancellationToken ct = default);
}
