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

        // 登录（不需令牌）→ 校验本地凭证、签发 JWT。权限由管理员显式维护（Deny-All 白名单，无自动放权）。
        app.MapPost("/api/auth/login", async (LoginRequest req, IAuthService auth, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(req.EmployeeId, req.Password, ct);

            // 登录审计：成功/失败都记一条（失败工号取自请求体，仅作审计线索）。
            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "Login",
                EmployeeId = result.Operator?.EmployeeId ?? (req.EmployeeId ?? "").Trim(),
                Status = result.Success ? "Success" : "Failed",
                Payload = result.Success ? null : result.Message,
                ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            return Results.Json(result);
        });

        // 受保护组：令牌校验 + 把工号绑进 HttpContext（身份以 token 为准，D2）
        var api = app.MapGroup("/api").AddEndpointFilter<TokenEndpointFilter>();

        // 本人改密（需令牌；工号以令牌为准，忽略请求体工号）
        api.MapPost("/auth/change-password", async (ChangePasswordRequest req, IAuthService auth, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var (ok, msg) = await auth.ChangePasswordAsync(empId, req.OldPassword, req.NewPassword, ct);

            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "ChangePassword",
                EmployeeId = empId,
                Status = ok ? "Success" : "Failed",
                Payload = ok ? null : msg,
                ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            return Results.Json(ok ? ApiResult.Ok(msg) : ApiResult.Fail(msg));
        });

        MapAdminApi(api);

        // 配置下发
        api.MapGet("/config", (HttpContext ctx, IConfigStore store) =>
            Results.Json(store.Get(ctx.GetEmployeeId())));

        // --- Remote-First 同步与操作接口 ---

        // 1. 同步清单 (基于权限白名单过滤)
        api.MapGet("/sync/catalog", async (HttpContext ctx, IPermissionService perm, TzhjDbContext db, IServerBatchStore store, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();

            // 当前用户的有效数据范围（角色展开的 (流程,组) 并集）
            var grants = await perm.GetGrantsAsync(empId, ct);
            if (grants.Count == 0) return Results.Json(new List<BatchCatalogItem>());

            var allowedFlows = grants.Select(g => g.Flow).ToHashSet();
            var batches = await db.BatchRegistries
                .Where(b => allowedFlows.Contains(b.Flow))
                .ToListAsync(ct);

            var catalog = new List<BatchCatalogItem>();
            foreach (var b in batches)
            {
                var allowed = grants.Any(g => g.Flow == b.Flow && (g.GroupName == "*" || g.GroupName == b.GroupName));
                if (!allowed) continue;

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
                                            HttpContext ctx, IPermissionService perm, IServerBatchStore store, CancellationToken ct) =>
        {
            if (!Enum.TryParse<FlowType>(flow, ignoreCase: true, out var flowType))
                return Results.BadRequest("Invalid flow.");

            var empId = ctx.GetEmployeeId();
            if (!await perm.HasAccessAsync(empId, flowType, groupName, ct)) return Results.Forbid();

            var stream = store.OpenFile(flowType, groupName, batchId, fileName);
            return stream is null ? Results.NotFound() : Results.File(stream, "application/octet-stream", fileName);
        });

        // 3. 行数据更新 (Remote-First 核心，带权限校验)
        api.MapPost("/batch/update-row", async ([FromBody] UpdateRowRequest req, HttpContext ctx, IPermissionService perm, IServerBatchStore store, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            if (!await perm.HasAccessAsync(empId, req.Flow, req.GroupName, ct)) return Results.Forbid();

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
        api.MapPost("/batch/suspend-exception", async ([FromBody] SuspendExceptionRequest req, HttpContext ctx, IPermissionService perm, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            if (!await perm.HasAccessAsync(empId, req.Flow, req.GroupName, ct)) return Results.Forbid();

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
        api.MapGet("/sync/exceptions", async (HttpContext ctx, IPermissionService perm, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            var grants = await perm.GetGrantsAsync(empId, ct);

            var list = new List<ExceptionItem>();
            foreach (var g in grants)
            {
                var groupExceptions = await db.Exceptions
                    .Where(e => e.Flow == g.Flow && (g.GroupName == "*" || e.GroupName == g.GroupName) && e.Status == RowStatus.Exception)
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
        api.MapPost("/batch/resolve-exception", async (string flow, string groupName, string batchId, string rowKey, HttpContext ctx, IPermissionService perm, TzhjDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse<FlowType>(flow, ignoreCase: true, out var flowType))
                return Results.BadRequest("Invalid flow.");

            var empId = ctx.GetEmployeeId();
            if (!await perm.HasAccessAsync(empId, flowType, groupName, ct)) return Results.Forbid();

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
        api.MapPost("/submit", async ([FromBody] SubmitRequest req, HttpContext ctx, IPermissionService perm, ISubmitSink sink, TzhjDbContext db, CancellationToken ct) =>
        {
            var empId = ctx.GetEmployeeId();
            if (!await perm.HasAccessAsync(empId, req.Flow, req.GroupName, ct)) return Results.Forbid();

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

    /// <summary>管理端（/api/admin/*，仅启用中的管理员）：用户与权限维护。</summary>
    private static void MapAdminApi(RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").AddEndpointFilter<AdminEndpointFilter>();

        // 用户列表（含所挂角色 + 展开后的有效数据范围，不含任何密码信息）
        admin.MapGet("/users", async (TzhjDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var users = await db.AppUsers.OrderBy(u => u.EmployeeId).ToListAsync(ct);
            var userRoles = await db.UserRoles
                .Include(ur => ur.Role!).ThenInclude(r => r.Permissions)
                .ToListAsync(ct);

            var list = users.Select(u =>
            {
                var mine = userRoles.Where(x => x.EmployeeId == u.EmployeeId).ToList();
                return new UserSummary
                {
                    EmployeeId = u.EmployeeId,
                    DisplayName = u.DisplayName,
                    Department = u.Department,
                    Position = u.Position,
                    IsActive = u.IsActive,
                    IsAdmin = u.IsAdmin,
                    MustChangePassword = u.MustChangePassword,
                    IsLocked = u.LockoutUntil is { } until && until > now,
                    Roles = mine.Select(x => new RoleRef { Id = x.RoleId, Name = x.Role!.Name }).ToList(),
                    EffectivePermissions = mine.SelectMany(x => x.Role!.Permissions)
                        .Select(p => new PermissionDto { Flow = p.Flow, GroupName = p.GroupName })
                        .DistinctBy(p => new { p.Flow, p.GroupName })
                        .ToList(),
                };
            }).ToList();

            return Results.Json(list);
        });

        // 新建用户（初始密码下发后首登强制改密）
        admin.MapPost("/users", async (CreateUserRequest req, IPasswordService pwd, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var empId = (req.EmployeeId ?? "").Trim();
            if (empId.Length == 0 || string.IsNullOrWhiteSpace(req.DisplayName))
                return Results.Json(ApiResult.Fail("工号和姓名不能为空。"), statusCode: StatusCodes.Status400BadRequest);
            if (string.IsNullOrEmpty(req.InitialPassword) || req.InitialPassword.Length < DbAuthService.MinPasswordLength)
                return Results.Json(ApiResult.Fail($"初始密码长度至少 {DbAuthService.MinPasswordLength} 位。"), statusCode: StatusCodes.Status400BadRequest);
            if (await db.AppUsers.AnyAsync(u => u.EmployeeId == empId, ct))
                return Results.Json(ApiResult.Fail($"工号 {empId} 已存在。"), statusCode: StatusCodes.Status409Conflict);

            db.AppUsers.Add(new AppUser
            {
                EmployeeId = empId,
                DisplayName = req.DisplayName.Trim(),
                Department = req.Department,
                Position = req.Position,
                PasswordHash = pwd.Hash(req.InitialPassword),
                IsActive = true,
                IsAdmin = req.IsAdmin,
                MustChangePassword = true,
            });
            LogAdmin(db, ctx, "AdminCreateUser", $"empId={empId}, admin={req.IsAdmin}");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已创建用户 {empId}。"));
        });

        // 重置密码（重置后首登强制改密、解锁）
        admin.MapPost("/users/{employeeId}/reset-password", async (string employeeId, ResetPasswordRequest req, IPasswordService pwd, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < DbAuthService.MinPasswordLength)
                return Results.Json(ApiResult.Fail($"新密码长度至少 {DbAuthService.MinPasswordLength} 位。"), statusCode: StatusCodes.Status400BadRequest);

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, ct);
            if (user is null) return Results.Json(ApiResult.Fail("用户不存在。"), statusCode: StatusCodes.Status404NotFound);

            user.PasswordHash = pwd.Hash(req.NewPassword);
            user.MustChangePassword = true;
            user.FailedAttempts = 0;
            user.LockoutUntil = null;
            user.UpdatedAt = DateTime.UtcNow;
            LogAdmin(db, ctx, "AdminResetPassword", $"empId={employeeId}");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已重置 {employeeId} 的密码。"));
        });

        // 启用/停用
        admin.MapPost("/users/{employeeId}/active", async (string employeeId, SetActiveRequest req, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            if (!req.IsActive && string.Equals(employeeId, ctx.GetEmployeeId(), StringComparison.Ordinal))
                return Results.Json(ApiResult.Fail("不能停用当前登录的管理员账号。"), statusCode: StatusCodes.Status400BadRequest);

            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, ct);
            if (user is null) return Results.Json(ApiResult.Fail("用户不存在。"), statusCode: StatusCodes.Status404NotFound);

            user.IsActive = req.IsActive;
            if (req.IsActive) { user.FailedAttempts = 0; user.LockoutUntil = null; }
            user.UpdatedAt = DateTime.UtcNow;
            LogAdmin(db, ctx, "AdminSetActive", $"empId={employeeId}, active={req.IsActive}");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已{(req.IsActive ? "启用" : "停用")} {employeeId}。"));
        });

        // 给用户挂角色（覆盖式）——数据可见范围只通过角色授予
        admin.MapPut("/users/{employeeId}/roles", async (string employeeId, SetUserRolesRequest req, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var user = await db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, ct);
            if (user is null) return Results.Json(ApiResult.Fail("用户不存在。"), statusCode: StatusCodes.Status404NotFound);

            var roleIds = req.RoleIds.Distinct().ToList();
            var validIds = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
            var missing = roleIds.Except(validIds).ToList();
            if (missing.Count > 0)
                return Results.Json(ApiResult.Fail($"角色不存在：{string.Join(",", missing)}"), statusCode: StatusCodes.Status400BadRequest);

            var existing = await db.UserRoles.Where(ur => ur.EmployeeId == employeeId).ToListAsync(ct);
            db.UserRoles.RemoveRange(existing);
            foreach (var rid in validIds)
                db.UserRoles.Add(new UserRole { EmployeeId = employeeId, RoleId = rid });

            LogAdmin(db, ctx, "AdminSetUserRoles", $"empId={employeeId}, roles=[{string.Join(",", validIds)}]");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已更新 {employeeId} 的角色。"));
        });

        // ---- 角色（数据范围捆绑）的增删改查 ----

        // 角色列表（含数据范围 + 挂载人数）
        admin.MapGet("/roles", async (TzhjDbContext db, CancellationToken ct) =>
        {
            var roles = await db.Roles.Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct);
            var counts = await db.UserRoles.GroupBy(ur => ur.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() }).ToListAsync(ct);

            var list = roles.Select(r => new RoleSummary
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Permissions = r.Permissions.Select(p => new PermissionDto { Flow = p.Flow, GroupName = p.GroupName }).ToList(),
                UserCount = counts.FirstOrDefault(c => c.RoleId == r.Id)?.Count ?? 0,
            }).ToList();
            return Results.Json(list);
        });

        // 新建角色
        admin.MapPost("/roles", async (SaveRoleRequest req, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Results.Json(ApiResult.Fail("角色名不能为空。"), statusCode: StatusCodes.Status400BadRequest);
            if (await db.Roles.AnyAsync(r => r.Name == name, ct))
                return Results.Json(ApiResult.Fail($"角色 {name} 已存在。"), statusCode: StatusCodes.Status409Conflict);

            var role = new Role { Name = name, Description = req.Description, Permissions = BuildRolePermissions(req.Permissions) };
            db.Roles.Add(role);
            LogAdmin(db, ctx, "AdminCreateRole", $"name={name}, ranges={role.Permissions.Count}");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已创建角色 {name}。"));
        });

        // 更新角色（名称/描述/数据范围覆盖式）
        admin.MapPut("/roles/{id:int}", async (int id, SaveRoleRequest req, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.Json(ApiResult.Fail("角色不存在。"), statusCode: StatusCodes.Status404NotFound);

            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Results.Json(ApiResult.Fail("角色名不能为空。"), statusCode: StatusCodes.Status400BadRequest);
            if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id, ct))
                return Results.Json(ApiResult.Fail($"角色 {name} 已存在。"), statusCode: StatusCodes.Status409Conflict);

            role.Name = name;
            role.Description = req.Description;
            role.UpdatedAt = DateTime.UtcNow;
            db.RolePermissions.RemoveRange(role.Permissions);
            role.Permissions.Clear();
            foreach (var rp in BuildRolePermissions(req.Permissions)) role.Permissions.Add(rp);

            LogAdmin(db, ctx, "AdminUpdateRole", $"id={id}, name={name}, ranges={role.Permissions.Count}");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已更新角色 {name}。"));
        });

        // 删除角色（级联删其数据范围与所有挂载）
        admin.MapDelete("/roles/{id:int}", async (int id, TzhjDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (role is null) return Results.Json(ApiResult.Fail("角色不存在。"), statusCode: StatusCodes.Status404NotFound);

            db.Roles.Remove(role);
            LogAdmin(db, ctx, "AdminDeleteRole", $"id={id}, name={role.Name}");
            await db.SaveChangesAsync(ct);
            return Results.Json(ApiResult.Ok($"已删除角色 {role.Name}。"));
        });
    }

    /// <summary>把入参的数据范围去重、去空，转成角色的 RolePermission 列表。</summary>
    private static List<RolePermission> BuildRolePermissions(List<PermissionDto> input) =>
        input.Where(p => !string.IsNullOrWhiteSpace(p.GroupName))
             .DistinctBy(p => new { p.Flow, p.GroupName })
             .Select(p => new RolePermission { Flow = p.Flow, GroupName = p.GroupName.Trim() })
             .ToList();

    private static void LogAdmin(TzhjDbContext db, HttpContext ctx, string action, string payload) =>
        db.ActivityLogs.Add(new ActivityLog
        {
            Action = action,
            EmployeeId = ctx.GetEmployeeId(),
            Status = "Success",
            Payload = payload,
            ClientIp = ctx.Connection.RemoteIpAddress?.ToString(),
            Timestamp = DateTime.UtcNow
        });

    private static bool TryParseDt(string s, out DateTime dt) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt);
}
