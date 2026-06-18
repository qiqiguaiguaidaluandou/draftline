using System.Windows.Threading;
using TZHJ.App.Services;
using TZHJ.Core.Enums;
using TZHJ.Infrastructure.Sync;

namespace TZHJ.App.Services;

/// <summary>
/// 会话内取数调度（登录态触发的真正落地）。登录成功后 <see cref="Start"/>：
///   · 立即补一轮 = **登录补拉**（补离线期间已关闭的窗）；
///   · 之后每 120s 轮询一次 = **会话内定时触发**（补在线期间新关闭的窗）。
/// 两者与手动补拉共用 <see cref="BatchSyncService"/>；幂等故轮询安全。拉到新批次弹 Toast
/// （列表刷新由 BatchListViewModel 的 FileSystemWatcher 负责）。覆盖操作员 AllowedFlows 的各流程。
/// </summary>
public sealed class SessionSyncScheduler
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(120);

    private readonly ISession _session;
    private readonly BatchSyncService _sync;
    private readonly IDialogService _dialog;
    private readonly DispatcherTimer _timer;
    private bool _busy;

    public SessionSyncScheduler(ISession session, BatchSyncService sync, IDialogService dialog)
    {
        _session = session;
        _sync = sync;
        _dialog = dialog;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await RunPassAsync();
    }

    /// <summary>登录成功后调用：立即补一轮（登录补拉）并开启 120s 轮询（会话内定时）。</summary>
    public void Start()
    {
        if (!_session.IsAuthenticated) return;
        _timer.Start();
        _ = RunPassAsync(isInitial: true); // 立即一轮 = 登录补拉（后台，不卡界面，缓冲 1min）
    }

    public void Stop() => _timer.Stop();

    private async Task RunPassAsync(bool isInitial = false)
    {
        if (_busy || !_session.IsAuthenticated) return; // 并发护栏
        _busy = true;
        try
        {
            var emp = _session.Operator.EmployeeId;

            // --- New Architecture: Pure Mirror Sync ---
            // 客户端不再主动计算缺失窗口并触发 /fetch。
            // 它的职责仅限于同步服务器上已经存在的批次、状态和异常。
            var result = await _sync.MirrorSyncAsync(emp);

            if (result.Fetched > 0)
            {
                _dialog.Info($"云端同步：新增 {result.Fetched} 个批次已到达。");
            }
        }
        catch (Exception ex)
        {
            // 镜像同步静默重试，不弹阻断错误，但记录到日志
            System.Diagnostics.Debug.WriteLine($"Mirror sync failed: {ex.Message}");
        }
        finally { _busy = false; }
    }

}
