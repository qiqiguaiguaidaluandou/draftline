using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using TZHJ.Core.Contracts;
using TZHJ.Core.Contracts.Http;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Gateway.AntiCorruption;
using TZHJ.Gateway.Auth;
using TZHJ.Gateway.Stores;

namespace TZHJ.Gateway.Endpoints;

/// <summary>无状态网关的全部端点（§4 契约）。逻辑都在防腐层/存储后面，端点只做转换。</summary>
public static class ApiEndpoints
{
    public static void MapTzhjApi(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok("ok"));

        // 登录（不需令牌）→ 占位认证签发令牌
        app.MapPost("/api/auth/login", async (LoginRequest req, IAuthService auth, TzhjDbContext db, IHostEnvironment env, HttpContext ctx) =>
        {
            var result = auth.Login(req.EmployeeId, req.Password);
            if (result.Success)
            {
                var empId = result.Operator!.EmployeeId;
                // [Auto-Seed] 仅开发环境自动补全权限，方便演示；生产环境遵循 doc ⑧ 的 Deny-All 白名单，
                // 未在 UserPermissions 显式授权的工号登录后取不到任何数据（权限须由 DBA / 后续管理后台维护）。
                if (env.IsDevelopment() && !await db.UserPermissions.AnyAsync(p => p.EmployeeId == empId))
                {
                    db.UserPermissions.AddRange(
                        new UserPermission { EmployeeId = empId, Flow = FlowType.Pricing, GroupName = "组1" },
                        new UserPermission { EmployeeId = empId, Flow = FlowType.DrawingSelection, GroupName = "组1" }
                    );
                }

                // 记录登录日志
                db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "Login",
                    EmployeeId = empId,
                    Status = "Success",
                    ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                    Timestamp = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
            }
            return Results.Json(result);
        });

        // 受保护组：令牌校验 + 把工号绑进 HttpContext（身份以 token 为准，D2）
        var api = app.MapGroup("/api").AddEndpointFilter<TokenEndpointFilter>();

        // 配置下发
        api.MapGet("/config", (HttpContext ctx, IConfigStore store) =>
            Results.Json(store.Get(ctx.GetEmployeeId())));

        // --- Remote-First 同步与操作接口 ---

        // 1. 同步清单 (基于权限白名单过滤)
        api.MapGet("/sync/catalog", async (HttpContext ctx, TzhjDbContext db, IServerBatchStore store, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            
            // 获取当前用户的权限
            var perms = await db.UserPermissions.Where(p => p.EmployeeId == empId).ToListAsync(ct);
            if (!perms.Any()) return Results.Json(new List<BatchCatalogItem>()); 

            var allowedFlows = perms.Select(p => p.Flow).ToHashSet();
            var batches = await db.BatchRegistries
                .Where(b => allowedFlows.Contains(b.Flow))
                .ToListAsync(ct);

            var catalog = new List<BatchCatalogItem>();
            foreach (var b in batches)
            {
                var p = perms.FirstOrDefault(p => p.Flow == b.Flow && (p.GroupName == "*" || p.GroupName == b.GroupName));
                if (p == null) continue; 

                var files = await store.ListFilesAsync(b.Flow, b.GroupName, b.BatchId, ct);
                catalog.Add(new BatchCatalogItem
                {
                    BatchId = b.BatchId,
                    GroupName = b.GroupName,
                    Flow = b.Flow,
                    Status = b.Status,
                    TotalRows = b.TotalRows, // 填充总行数
                    LastModified = b.LastModified,
                    Files = files
                });
            }
            return Results.Json(catalog);
        });

        // 2. 文件下载 (带权限校验)
        api.MapGet("/sync/download", async (string flow, string groupName, string batchId, string fileName, 
                                            HttpContext ctx, IServerBatchStore store, TzhjDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<FlowType>(flow, ignoreCase: true, out var flowType))
                return Results.BadRequest("Invalid flow.");

            var empId = ctx.GetEmployeeId();
            var hasPerm = await db.UserPermissions.AnyAsync(p => 
                p.EmployeeId == empId && p.Flow == flowType && (p.GroupName == "*" || p.GroupName == groupName), ct);
            if (!hasPerm) return Results.Forbid();

            var stream = store.OpenFile(flowType, groupName, batchId, fileName);
            return stream is null ? Results.NotFound() : Results.File(stream, "application/octet-stream", fileName);
        });

        // 3. 行数据更新 (Remote-First 核心，带权限校验)
        api.MapPost("/batch/update-row", async ([FromBody] UpdateRowRequest req, HttpContext ctx, IServerBatchStore store, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var hasPerm = await db.UserPermissions.AnyAsync(p => 
                p.EmployeeId == empId && p.Flow == req.Flow && (p.GroupName == "*" || p.GroupName == req.GroupName), ct);
            if (!hasPerm) return Results.Forbid();

            var registry = await db.BatchRegistries.FirstOrDefaultAsync(b => b.BatchId == req.BatchId && b.GroupName == req.GroupName && b.Flow == req.Flow, ct);
            if (registry == null) 
            {
                return Results.NotFound(new { message = $"云端找不到该批次记录。BatchId: {req.BatchId}, Group: {req.GroupName}" });
            }

            await store.UpdateExcelRowAsync(registry.Flow, req.GroupName, req.BatchId, req.RowKey, req.Values, ct);
            registry.LastModified = DateTime.UtcNow;

            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "UpdateRow",
                EmployeeId = empId,
                Flow = req.Flow,
                GroupName = req.GroupName,
                BatchId = req.BatchId,
                ImpactCount = 1,
                Status = "Success",
                Payload = $"Row: {req.RowKey}",
                ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // 4. 异常挂起
        api.MapPost("/batch/suspend-exception", async ([FromBody] SuspendExceptionRequest req, HttpContext ctx, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var hasPerm = await db.UserPermissions.AnyAsync(p => 
                p.EmployeeId == empId && p.Flow == req.Flow && (p.GroupName == "*" || p.GroupName == req.GroupName), ct);
            if (!hasPerm) return Results.Forbid();

            var entity = new ExceptionEntity
            {
                GroupName = req.GroupName,
                Flow = req.Flow,
                RowKey = req.RowKey,
                MaterialCode = req.MaterialCode,
                DisplayName = req.DisplayName,
                SourceBatch = req.BatchId,
                Reason = req.Reason,
                Status = RowStatus.Exception,
                SuspendedAt = DateTime.UtcNow
            };
            db.Exceptions.Add(entity);

            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "Suspend",
                EmployeeId = empId,
                Flow = req.Flow,
                GroupName = req.GroupName,
                BatchId = req.BatchId,
                ImpactCount = 1,
                Status = "Success",
                Payload = $"Row: {req.RowKey}, Reason: {req.Reason}",
                ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // 5. 异常池查询 (组内共享视野)
        api.MapGet("/sync/exceptions", async (HttpContext ctx, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var perms = await db.UserPermissions.Where(p => p.EmployeeId == empId).ToListAsync(ct);
            
            var list = new List<ExceptionItem>();
            foreach (var p in perms)
            {
                var groupExceptions = await db.Exceptions
                    .Where(e => e.Flow == p.Flow && (p.GroupName == "*" || e.GroupName == p.GroupName) && e.Status == RowStatus.Exception)
                    .Select(e => new ExceptionItem
                    {
                        Flow = e.Flow,
                        RowKey = e.RowKey,
                        GroupName = e.GroupName,
                        MaterialCode = e.MaterialCode,
                        DisplayName = e.DisplayName,
                        SourceBatch = e.SourceBatch,
                        Reason = e.Reason,
                        SuspendedAt = e.SuspendedAt
                    })
                    .ToListAsync(ct);
                list.AddRange(groupExceptions);
            }

            return Results.Json(list.DistinctBy(x => new { x.SourceBatch, x.RowKey }));
        });

        // 6. 异常处理 (补回传或撤销)
        api.MapPost("/batch/resolve-exception", async (string flow, string groupName, string batchId, string rowKey, HttpContext ctx, TzhjDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<FlowType>(flow, ignoreCase: true, out var flowType))
                return Results.BadRequest("Invalid flow.");

            var empId = ctx.GetEmployeeId();
            var hasPerm = await db.UserPermissions.AnyAsync(p =>
                p.EmployeeId == empId && p.Flow == flowType && (p.GroupName == "*" || p.GroupName == groupName), ct);
            if (!hasPerm) return Results.Forbid();

            var entity = await db.Exceptions.FirstOrDefaultAsync(e => e.Flow == flowType && e.SourceBatch == batchId && e.RowKey == rowKey && e.GroupName == groupName, ct);
            if (entity == null && groupName != "Default")
                entity = await db.Exceptions.FirstOrDefaultAsync(e => e.Flow == flowType && e.SourceBatch == batchId && e.RowKey == rowKey && e.GroupName == "Default", ct);

            if (entity != null)
            {
                entity.Status = RowStatus.Uploaded; // 逻辑删除/处理完成
                entity.ResolvedAt = DateTime.UtcNow;
                entity.ResolvedBy = empId;

                db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "Resolve",
                    EmployeeId = empId,
                    Flow = entity.Flow,
                    GroupName = groupName,
                    BatchId = batchId,
                    ImpactCount = 1,
                    Status = "Success",
                    Payload = $"Row: {rowKey}",
                    ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                    Timestamp = DateTime.UtcNow
                });

                await db.SaveChangesAsync(ct);
            }
            return Results.Ok();
        });

        // 回传：整批正常行 → SRM/EBS（成功判定 + 幂等 + 失败留痕，见 changes/009）
        api.MapPost("/submit", async ([FromBody] SubmitRequest req, HttpContext ctx, ISubmitSink sink, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var target = req.Flow == FlowType.Pricing ? "SRM" : "EBS";

            var registry = await db.BatchRegistries.FirstOrDefaultAsync(b => b.BatchId == req.BatchKey && b.Flow == req.Flow && b.GroupName == req.GroupName, ct);
            if (registry == null) return Results.NotFound(new { message = "云端找不到批次记录。" });

            // 幂等：已提交过的批次不再回传外部系统，直接回显既有 AuditId（防网络重发 / 重复点提交）。
            if (registry.Status == BatchLocation.Done)
            {
                return Results.Json(new SubmitResult
                {
                    Success = true,
                    AuditId = registry.AuditId,
                    Message = "该批次此前已提交，未重复回传。",
                });
            }

            // 失败留痕：整批失败或任一行失败都记 Failed 日志、不置 Done，返回各行结果供客户端重试。
            IReadOnlyList<SubmitRowResult> rowResults;
            if (sink.ShouldFailBatch())
            {
                rowResults = req.Rows.Select(r => new SubmitRowResult { RowKey = r.RowKey, Success = false }).ToList();
            }
            else
            {
                rowResults = await sink.SubmitAsync(req.Flow, empId, req.Rows, ct);
            }

            var failedKeys = rowResults.Where(r => !r.Success).Select(r => r.RowKey).ToList();
            if (failedKeys.Count > 0)
            {
                db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "Submit",
                    EmployeeId = empId,
                    Flow = req.Flow,
                    GroupName = req.GroupName,
                    BatchId = req.BatchKey,
                    ImpactCount = rowResults.Count(r => r.Success),
                    Status = "Failed",
                    Payload = $"Target: {target}, FailedRows: {string.Join(",", failedKeys)}",
                    ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                    Timestamp = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);

                return Results.Json(new SubmitResult
                {
                    Success = false,
                    RowResults = rowResults.ToList(),
                    Message = $"回传 {target} 失败 {failedKeys.Count} 行，批次未完成，可重试。",
                });
            }

            // 成功：置 Done + 结构化审计（窗口起止 / AuditId 落独立列，供 audit/exists 精确查）。
            var auditId = $"AUDIT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..36];
            registry.Status = BatchLocation.Done;
            registry.AuditId = auditId;
            registry.LastModified = DateTime.UtcNow;

            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "Submit",
                EmployeeId = empId,
                Flow = req.Flow,
                GroupName = req.GroupName,
                BatchId = req.BatchKey,
                ImpactCount = req.Rows.Count,
                Status = "Success",
                WindowStart = req.WindowStart.ToUniversalTime(),
                WindowEnd = req.WindowEnd.ToUniversalTime(),
                AuditId = auditId,
                Payload = $"Target: {target}",
                ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);

            return Results.Json(new SubmitResult
            {
                Success = true,
                AuditId = auditId,
                RowResults = rowResults.ToList(),
                Message = $"已回传 {req.Rows.Count} 行至 {target}。",
            });
        });

        // 用户操作日志：查本人记录（从统一 ActivityLogs 捞动作）
        api.MapGet("/oplog/mine", async (HttpContext ctx, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var logs = await db.ActivityLogs
                .Where(x => x.EmployeeId == empId)
                .OrderByDescending(x => x.Timestamp)
                .Select(x => new OperationLogEntry
                {
                    EmployeeId = x.EmployeeId,
                    Operation = x.Action + (x.Payload != null ? ": " + x.Payload : ""),
                    FormName = x.BatchId ?? "",
                    Flow = x.Flow ?? FlowType.Pricing,
                    ClientIp = x.ClientIp,
                    OperatedAt = DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc).ToLocalTime(),
                })
                .ToListAsync(ct);

            return Results.Json(new OperationLogListResponse { Items = logs });
        });

        // 登录补拉判据：按结构化窗口列精确查成功回传记录（009，取代 Payload 文本匹配）
        api.MapGet("/audit/exists", async (string flow, string windowStart, string windowEnd, HttpContext ctx, TzhjDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<FlowType>(flow, ignoreCase: true, out var flowType)) return Results.BadRequest();
            if (!TryParseDt(windowStart, out var ws) || !TryParseDt(windowEnd, out var we)) return Results.BadRequest();

            var wsUtc = ws.ToUniversalTime();
            var weUtc = we.ToUniversalTime();
            var empId = ctx.GetEmployeeId();

            var hit = await db.ActivityLogs
                .Where(r => r.Flow == flowType && r.EmployeeId == empId && r.Action == "Submit" && r.Status == "Success"
                            && r.WindowStart == wsUtc && r.WindowEnd == weUtc)
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefaultAsync(ct);

            return Results.Json(new AuditExistsResponse
            {
                Exists = hit != null,
                AuditId = hit?.AuditId
            });
        });
    }

    private static bool TryParseDt(string s, out DateTime dt) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt);
}
