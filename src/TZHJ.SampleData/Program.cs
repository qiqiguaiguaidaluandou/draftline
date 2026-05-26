using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Core.Schemas;
using TZHJ.Infrastructure.Gateways.Mock;
using TZHJ.Infrastructure.Options;
using TZHJ.Infrastructure.Storage;

// ---------- 解析参数 ----------
var root = GetArg(args, "--root") ?? Path.Combine(Directory.GetCurrentDirectory(), "TZHJ_Data");
var employee = GetArg(args, "--employee") ?? "10086";
root = Path.GetFullPath(root);

Console.WriteLine($"样例数据根目录：{root}");
Console.WriteLine($"工号：{employee}");
Console.WriteLine(new string('-', 60));

// ---------- 组装 Mock 网关 + 本地存储（直接 new，不走 DI） ----------
var mock = new MockOptions { LocalRoot = root, LoginDelayMs = 0, FetchDelayMs = 0, SubmitDelayMs = 0 };
var storage = new LocalStorageOptions { Root = root };
var fields = new DefaultFieldProvider();
IDataGateway data = new MockDataGateway(mock);
ILocalBatchStore store = new LocalBatchStore(fields, storage);

var rng = new Random(mock.Seed);
string[] reasonsPricing = { "图纸缺失", "价格待定", "图纸版本不符" };
string[] reasonsDrawing = { "图纸不清晰", "图纸缺失", "信息不全" };

foreach (var flow in new[] { FlowType.Pricing, FlowType.DrawingSelection })
{
    Console.WriteLine($"\n=== 生成 {(flow == FlowType.Pricing ? "核价" : "挑图")} 流程数据 ===");

    var windows = RecentWindows(flow, count: 4);

    for (var i = 0; i < windows.Count; i++)
    {
        var (start, end) = windows[i];
        var fetched = await data.FetchBatchAsync(new FetchRequest
        {
            EmployeeId = employee, Flow = flow, WindowStart = start, WindowEnd = end,
        });
        var batch = await store.WriteFetchedBatchAsync(fetched);

        // i=0 → 待处理·未处理（全 Pending）
        // i=1 → 待处理·处理中（部分 Done）
        // i>=2 → 已处理（全部 Done/Exception，回传后移入已处理）
        string state;
        if (i == 0)
        {
            state = "待处理·未处理";
        }
        else if (i == 1)
        {
            ProcessRows(batch, flow, rng, doneRatio: 0.6, excRatio: 0.0);
            await store.SaveBatchAsync(batch);
            state = "待处理·处理中";
        }
        else
        {
            var reasons = flow == FlowType.Pricing ? reasonsPricing : reasonsDrawing;
            ProcessRows(batch, flow, rng, doneRatio: 1.0, excRatio: 0.2, reasons);
            await store.SaveBatchAsync(batch);

            var exceptions = batch.Rows
                .Where(r => r.Status == RowStatus.Exception)
                .Select(r => new ExceptionItem
                {
                    Flow = flow,
                    RowKey = r.RowKey,
                    MaterialCode = r.Get("materialCode") ?? r.RowKey,
                    DisplayName = r.Get("name") ?? r.Get("materialDesc"),
                    SourceBatch = batch.FolderName,
                    Reason = r.ExceptionReason ?? "未知",
                    SuspendedAt = DateTime.Now,
                });
            await store.AddExceptionsAsync(flow, employee, exceptions);

            await store.MoveToDoneAsync(batch);
            state = $"已处理（正常 {batch.DoneCount} / 异常 {batch.ExceptionCount}）";
        }

        Console.WriteLine($"  · {batch.FolderName}  物料 {batch.MaterialCount}  → {state}");
    }
}

Console.WriteLine(new string('-', 60));
Console.WriteLine("完成。可用 tree / 资源管理器查看，或交给 WPF 客户端读取。");
return 0;


// ---------- 局部函数 ----------

// 取最近 count 个具体时间窗（按结束时间倒序：最新在前）。
List<(DateTime Start, DateTime End)> RecentWindows(FlowType flow, int count)
{
    var defs = CollectionSchedules.For(flow);
    var today = DateOnly.FromDateTime(DateTime.Today);
    var list = new List<(DateTime, DateTime)>();
    foreach (var anchor in new[] { today, today.AddDays(-1) })
        foreach (var w in defs)
            list.Add(w.Resolve(anchor));
    return list.OrderByDescending(x => x.Item2).Take(count).ToList();
}

// 模拟操作员作业：按比例把行置为已处理（填待填列）/挂起异常；无图纸的行优先挂"图纸缺失"。
void ProcessRows(Batch batch, FlowType flow, Random r, double doneRatio, double excRatio, string[]? reasons = null)
{
    var manual = FieldSchemas.ManualFields(flow).ToList();
    foreach (var row in batch.Rows)
    {
        var roll = r.NextDouble();
        var noDrawing = row.Drawings.Count == 0;

        if (noDrawing || roll < excRatio)
        {
            row.Status = RowStatus.Exception;
            row.ExceptionReason = noDrawing ? "图纸缺失"
                : reasons is { Length: > 0 } ? reasons[r.Next(reasons.Length)] : "待跟进";
        }
        else if (roll < doneRatio)
        {
            foreach (var f in manual)
                row.Set(f.Key, f.Editor == FieldEditor.Number
                    ? Math.Round(r.NextDouble() * 100 + 5, 2).ToString("0.00")
                    : (r.Next(2) == 0 ? "是" : "否"));
            row.Status = RowStatus.Done;
        }
        // 否则保持 Pending
    }
}

static string? GetArg(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}
