# 开发文档-⑥ 操作日志 PostgreSQL 持久化实施细节

## 1. 目标
将目前基于 JSONL 文件的 `FileOperationLogStore` 迁移至 PostgreSQL 数据库。
*   **原因**：统一数据存储，提升查询性能（特别是操作员查看“我的操作日志”时），方便未来基于 SQL 的多维度统计分析。
*   **策略**：新建 `PgOperationLogStore` 实现类，替换原有的文件实现，利用现有的 EF Core 基础设施（`TzhjDbContext`）。

---

## 2. 数据库设计

### 2.1 实体模型设计 (Entity)
在 `src/TZHJ.Gateway/Stores/` 目录下创建 `OperationLogEntity.cs`，对应数据库中的 `operation_logs` 表。

**表结构 (`operation_logs`)**：
*   **`log_id`** (BIGSERIAL / long, PK): 自增主键。
*   **`employee_id`** (VARCHAR(50)): 操作员工号（创建索引优化查询）。
*   **`operation`** (VARCHAR(100)): 按钮动作（如“回传到SRM”、“补回传”）。
*   **`form_name`** (VARCHAR(200)): 操作界面/批次名。
*   **`flow`** (INTEGER): 流程类型枚举。
*   **`client_ip`** (VARCHAR(50)): 客户端 IP 地址。
*   **`operated_at`** (TIMESTAMPTZ): 操作发生时间（UTC）。

### 2.2 TzhjDbContext 扩展
在现有的 `TzhjDbContext` 中添加 DbSet 并配置索引：
*   为 `employee_id` 建立索引 `idx_oplog_employee`。
*   为 `operated_at` 建立索引 `idx_oplog_time`。

---

## 3. 代码实施步骤

### 步骤 1：创建 `OperationLogEntity.cs`
定义实体类并使用 Data Annotations 配置表名和列名映射。

### 步骤 2：创建 `PgOperationLogStore.cs`
实现 `IOperationLogStore` 接口：
*   **Append()**：将传入的 `OperationLogEntry` 转换为实体并保存。**强制转换 `OperatedAt` 为 UTC**。
*   **ListByEmployee()**：按工号查询，并按时间倒序排列。

### 步骤 3：更新依赖注入 (DI)
在 `Program.cs` 中：
*   将 `IOperationLogStore` 的注册从 `FileOperationLogStore` (Singleton) 切换为 `PgOperationLogStore` (Scoped)。

### 步骤 4：数据库迁移
1.  生成迁移：`dotnet ef migrations add AddOperationLogs`
2.  同步数据库：`dotnet ef database update`

---

## 4. 影响评估与兼容性
*   **旧数据**：现有通过文件记录的旧日志不会自动迁移至数据库。鉴于目前处于开发阶段，旧文件日志可作为历史存档保留或直接废弃。
*   **回滚逻辑**：若数据库出现异常，可通过修改 `Program.cs` 中的一行 DI 注册代码瞬间切回文件存储模式，具备良好的容灾性。

---

**文档维护**：Gemini CLI
**最后更新**：2026-06-09
