namespace TZHJ.Core.Enums;

/// <summary>
/// 行级状态（存于批次 manifest）。状态机：
/// 待处理 → 已处理(暂存,本地) → 已上传；或 待处理 → 挂起异常 → 进异常待跟进池。
/// </summary>
public enum RowStatus
{
    /// <summary>待处理：取数后初始状态，待填列尚未填写/确认。</summary>
    Pending,

    /// <summary>已处理：待填列已填并确认（核价=已核价 / 挑图=已填写），暂存在本地，尚未回传。</summary>
    Done,

    /// <summary>挂起异常：整批提交时不回传，转入本地异常待跟进池。</summary>
    Exception,

    /// <summary>已上传：整批提交、后端回传成功。</summary>
    Uploaded,
}
