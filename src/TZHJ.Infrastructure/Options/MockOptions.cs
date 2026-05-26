namespace TZHJ.Infrastructure.Options;

/// <summary>
/// Mock 网关的行为参数。故意带可配延迟与随机失败，好让加载态/回传失败/部分行异常这些 UI 分支
/// 在没有真接口时也能调全。来自 appsettings.json 的 "Mock" 节。
/// </summary>
public sealed class MockOptions
{
    /// <summary>本地数据根目录（Mock 配置网关下发；WPF 默认 D:\TZHJ_Data，样例生成器用 CLI 指定）。</summary>
    public string LocalRoot { get; set; } = "TZHJ_Data";

    public int LoginDelayMs { get; set; } = 400;
    public int FetchDelayMs { get; set; } = 600;
    public int SubmitDelayMs { get; set; } = 800;

    /// <summary>每批次生成行数下限/上限。</summary>
    public int MinRowsPerBatch { get; set; } = 4;
    public int MaxRowsPerBatch { get; set; } = 9;

    /// <summary>某行图纸"缺失"的概率（用于触发完整性校验/挂起异常演示），0~1。</summary>
    public double DrawingMissingRate { get; set; } = 0.12;

    /// <summary>整批回传失败概率，0~1（0 = 永不失败）。</summary>
    public double SubmitFailureRate { get; set; } = 0.0;

    /// <summary>随机种子（固定可复现）。</summary>
    public int Seed { get; set; } = 20260525;
}
