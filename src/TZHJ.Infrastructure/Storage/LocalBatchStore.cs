using System.Text.Json;
using System.Text.Json.Serialization;
using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Infrastructure.Options;

namespace TZHJ.Infrastructure.Storage;

/// <summary>
/// 本地批次存储实现。文件夹即真相源：批次列表/状态映射 {待处理|已处理} 目录，
/// 行级状态在 manifest，待处理→已处理 仅由回传成功驱动（MoveToDoneAsync）。
/// </summary>
public sealed class LocalBatchStore : ILocalBatchStore
{
    private const string MaterialCodeKey = "materialCode";
    private static readonly string[] NameKeys = { "name", "materialDesc" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFieldProvider _fields;
    private readonly LocalStorageOptions _storage;

    public LocalBatchStore(IFieldProvider fields, LocalStorageOptions storage)
    {
        _fields = fields;
        _storage = storage;
    }

    private string Root => _storage.Root;

    // ---------- 列表 ----------

    public async Task<IReadOnlyList<Batch>> ListBatchesAsync(
        FlowType flow, string employeeId, BatchLocation location, CancellationToken ct = default)
    {
        var dir = LocalPaths.LocationRoot(Root, flow, employeeId, location);
        var batches = new List<Batch>();

        if (Directory.Exists(dir))
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var folderName = Path.GetFileName(sub);
                if (!LocalPaths.TryParseFolderName(folderName, out var start, out var end))
                    continue;

                var manifest = await BatchManifest.LoadAsync(Path.Combine(sub, LocalFolders.Manifest), ct);

                var rows = manifest is not null
                    ? manifest.Rows.Select(m => new MaterialRow
                    {
                        RowKey = m.RowKey,
                        Status = m.Status,
                        ExceptionReason = m.ExceptionReason,
                    }).ToList()
                    : LoadRowsFromXlsx(sub, flow); // 无 manifest：从 xlsx 数行，状态默认待处理

                batches.Add(new Batch
                {
                    Flow = flow,
                    EmployeeId = employeeId,
                    WindowStart = start,
                    WindowEnd = end,
                    FolderName = folderName,
                    FolderPath = sub,
                    Location = location,
                    Rows = rows,
                    FetchedAt = manifest?.FetchedAt ?? Directory.GetCreationTime(sub),
                    SubmittedAt = manifest?.SubmittedAt,
                });
            }
        }

        return batches.OrderByDescending(b => b.WindowStart).ToList();
    }

    // ---------- 读取完整批次 ----------

    public async Task<Batch?> GetBatchAsync(
        FlowType flow, string employeeId, BatchLocation location, string folderName, CancellationToken ct = default)
    {
        var dir = LocalPaths.BatchDir(Root, flow, employeeId, location, folderName);
        if (!Directory.Exists(dir)) return null;
        if (!LocalPaths.TryParseFolderName(folderName, out var start, out var end)) return null;

        var fields = _fields.FieldsFor(flow);
        var xlsxRows = ExcelGridIO.Read(Path.Combine(dir, LocalFolders.GridWorkbook), fields);
        var manifest = await BatchManifest.LoadAsync(Path.Combine(dir, LocalFolders.Manifest), ct);
        var manifestByKey = manifest?.Rows.ToDictionary(m => m.RowKey) ?? new();

        var rows = new List<MaterialRow>();
        foreach (var (rowKey, values) in xlsxRows)
        {
            var row = new MaterialRow { RowKey = rowKey, Values = values };
            var materialCode = values.GetValueOrDefault(MaterialCodeKey) ?? rowKey;

            if (manifestByKey.TryGetValue(rowKey, out var mr))
            {
                row.Status = mr.Status;
                row.ExceptionReason = mr.ExceptionReason;
                // 完整性校验：manifest 期望图纸 vs 磁盘实际
                foreach (var fileName in mr.Drawings)
                    row.Drawings.Add(MakeDrawingRef(dir, fileName, materialCode));
            }
            else
            {
                // 无 manifest 记录：扫盘按物料编码前缀找图
                foreach (var file in ScanDrawings(dir, materialCode))
                    row.Drawings.Add(MakeDrawingRef(dir, Path.GetFileName(file), materialCode));
            }
            rows.Add(row);
        }

        return new Batch
        {
            Flow = flow,
            EmployeeId = employeeId,
            WindowStart = start,
            WindowEnd = end,
            FolderName = folderName,
            FolderPath = dir,
            Location = location,
            Rows = rows,
            FetchedAt = manifest?.FetchedAt ?? Directory.GetCreationTime(dir),
            SubmittedAt = manifest?.SubmittedAt,
        };
    }

    // ---------- 落本地（取数结果 → 待处理批次） ----------

    public async Task<Batch> WriteFetchedBatchAsync(FetchResult fetched, CancellationToken ct = default)
    {
        var fields = _fields.FieldsFor(fetched.Flow);
        var folderName = LocalPaths.BatchFolderName(fetched.WindowStart, fetched.WindowEnd);
        var dir = LocalPaths.BatchDir(Root, fetched.Flow, fetched.EmployeeId, BatchLocation.Todo, folderName);
        Directory.CreateDirectory(dir);

        var rows = new List<MaterialRow>();
        var manifestRows = new List<ManifestRow>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fr in fetched.Rows)
        {
            var row = new MaterialRow
            {
                RowKey = fr.RowKey,
                Values = new Dictionary<string, string?>(fr.Values),
                Status = RowStatus.Pending,
            };
            var materialCode = fr.Values.GetValueOrDefault(MaterialCodeKey) ?? fr.RowKey;

            var drawingNames = new List<string>();
            foreach (var d in fr.Drawings)
            {
                var fileName = EnsureUnique(d.FileName, usedNames);
                await File.WriteAllBytesAsync(Path.Combine(dir, fileName), d.Content, ct);
                drawingNames.Add(fileName);
                row.Drawings.Add(MakeDrawingRef(dir, fileName, materialCode));
            }

            rows.Add(row);
            manifestRows.Add(new ManifestRow
            {
                RowKey = fr.RowKey,
                MaterialCode = materialCode,
                DisplayName = DisplayNameOf(fr.Values),
                Status = RowStatus.Pending,
                Drawings = drawingNames,
            });
        }

        ExcelGridIO.Write(Path.Combine(dir, LocalFolders.GridWorkbook), fields, rows);

        var manifest = new BatchManifest
        {
            Flow = fetched.Flow,
            EmployeeId = fetched.EmployeeId,
            WindowStart = fetched.WindowStart,
            WindowEnd = fetched.WindowEnd,
            FetchedAt = DateTime.Now,
            Rows = manifestRows,
        };
        await BatchManifest.SaveAsync(Path.Combine(dir, LocalFolders.Manifest), manifest, ct);

        return new Batch
        {
            Flow = fetched.Flow,
            EmployeeId = fetched.EmployeeId,
            WindowStart = fetched.WindowStart,
            WindowEnd = fetched.WindowEnd,
            FolderName = folderName,
            FolderPath = dir,
            Location = BatchLocation.Todo,
            Rows = rows,
            FetchedAt = manifest.FetchedAt,
        };
    }

    // ---------- 暂存（写回 xlsx + manifest） ----------

    public async Task SaveBatchAsync(Batch batch, CancellationToken ct = default)
    {
        var fields = _fields.FieldsFor(batch.Flow);
        ExcelGridIO.Write(Path.Combine(batch.FolderPath, LocalFolders.GridWorkbook), fields, batch.Rows);

        var manifestPath = Path.Combine(batch.FolderPath, LocalFolders.Manifest);
        var manifest = await BatchManifest.LoadAsync(manifestPath, ct) ?? new BatchManifest
        {
            Flow = batch.Flow,
            EmployeeId = batch.EmployeeId,
            WindowStart = batch.WindowStart,
            WindowEnd = batch.WindowEnd,
            FetchedAt = batch.FetchedAt,
        };

        manifest.Rows = batch.Rows.Select(r => new ManifestRow
        {
            RowKey = r.RowKey,
            MaterialCode = r.Get(MaterialCodeKey) ?? r.RowKey,
            DisplayName = DisplayNameOf(r.Values),
            Status = r.Status,
            ExceptionReason = r.ExceptionReason,
            Drawings = r.Drawings.Select(d => d.FileName).ToList(),
        }).ToList();

        await BatchManifest.SaveAsync(manifestPath, manifest, ct);
    }

    // ---------- 待处理 → 已处理 ----------

    public async Task MoveToDoneAsync(Batch batch, CancellationToken ct = default)
    {
        var destRoot = LocalPaths.LocationRoot(Root, batch.Flow, batch.EmployeeId, BatchLocation.Done);
        Directory.CreateDirectory(destRoot);
        var dest = Path.Combine(destRoot, batch.FolderName);

        if (Directory.Exists(dest))
            Directory.Delete(dest, recursive: true); // 极少见：目标重名，覆盖
        Directory.Move(batch.FolderPath, dest);

        var manifestPath = Path.Combine(dest, LocalFolders.Manifest);
        var manifest = await BatchManifest.LoadAsync(manifestPath, ct);
        if (manifest is not null)
        {
            manifest.SubmittedAt = DateTime.Now;
            await BatchManifest.SaveAsync(manifestPath, manifest, ct);
        }
    }

    // ---------- 异常待跟进池 ----------

    public async Task AddExceptionsAsync(
        FlowType flow, string employeeId, IEnumerable<ExceptionItem> items, CancellationToken ct = default)
    {
        var poolDir = LocalPaths.ExceptionPoolRoot(Root, flow, employeeId);
        Directory.CreateDirectory(poolDir);
        var file = Path.Combine(poolDir, LocalFolders.ExceptionPoolFile);

        var list = (await ReadExceptionsAsync(file, ct)).ToList();
        list.AddRange(items);

        await using var fs = File.Create(file);
        await JsonSerializer.SerializeAsync(fs, list, JsonOpts, ct);
    }

    public async Task<IReadOnlyList<ExceptionItem>> ListExceptionsAsync(
        FlowType flow, string employeeId, CancellationToken ct = default)
    {
        var file = Path.Combine(LocalPaths.ExceptionPoolRoot(Root, flow, employeeId), LocalFolders.ExceptionPoolFile);
        var list = await ReadExceptionsAsync(file, ct);
        return list.OrderByDescending(e => e.SuspendedAt).ToList();
    }

    public bool BatchExists(FlowType flow, string employeeId, DateTime windowStart, DateTime windowEnd)
    {
        var folderName = LocalPaths.BatchFolderName(windowStart, windowEnd);
        return Directory.Exists(LocalPaths.BatchDir(Root, flow, employeeId, BatchLocation.Todo, folderName))
            || Directory.Exists(LocalPaths.BatchDir(Root, flow, employeeId, BatchLocation.Done, folderName));
    }

    // ---------- 私有辅助 ----------

    private static async Task<List<ExceptionItem>> ReadExceptionsAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return new();
        await using var fs = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<List<ExceptionItem>>(fs, JsonOpts, ct) ?? new();
    }

    private List<MaterialRow> LoadRowsFromXlsx(string dir, FlowType flow)
    {
        var fields = _fields.FieldsFor(flow);
        return ExcelGridIO.Read(Path.Combine(dir, LocalFolders.GridWorkbook), fields)
            .Select(x => new MaterialRow { RowKey = x.RowKey, Values = x.Values, Status = RowStatus.Pending })
            .ToList();
    }

    private static DrawingRef MakeDrawingRef(string dir, string fileName, string materialCode) => new()
    {
        FileName = fileName,
        MaterialCode = materialCode,
        Kind = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant(),
        Exists = File.Exists(Path.Combine(dir, fileName)),
    };

    private static IEnumerable<string> ScanDrawings(string dir, string materialCode)
    {
        var prefix = materialCode + "__";
        return Directory.EnumerateFiles(dir)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                       && !name.Equals(LocalFolders.GridWorkbook, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string? DisplayNameOf(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var k in NameKeys)
            if (values.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v))
                return v;
        return null;
    }

    private static string EnsureUnique(string fileName, HashSet<string> used)
    {
        if (used.Add(fileName)) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem}({i}){ext}";
            if (used.Add(candidate)) return candidate;
        }
    }
}
