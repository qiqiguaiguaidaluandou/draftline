namespace Draftline.Core.Logging;

/// <summary>
/// <c>ActivityLogs.Action</c> 列的全部取值——单一常量源。写入点、清理条件、查询、显示词表
/// （<see cref="LogText"/>）统一引用此处，杜绝散落的魔法字符串（拼错即编译错，不再是运行期沉默失配）。
/// </summary>
public static class LogActions
{
    // —— 操作员业务动作（服务端权威记录）——
    public const string Login = "Login";
    public const string ChangePassword = "ChangePassword";
    public const string UpdateRow = "UpdateRow";
    public const string Suspend = "Suspend";
    public const string Resolve = "Resolve";
    public const string RefetchDrawing = "RefetchDrawing";

    /// <summary>回传（唯一填 WindowStart/End/AuditId 结构化列，见 #009）。</summary>
    public const string Submit = "Submit";

    // —— 系统 ——
    /// <summary>系统取数（EmployeeId="SYSTEM"）。</summary>
    public const string Ingest = "Ingest";

    // —— 后台管理 ——
    public const string AdminLogin = "AdminLogin";
    public const string AdminCreateUser = "AdminCreateUser";
    public const string AdminResetPassword = "AdminResetPassword";
    public const string AdminSetActive = "AdminSetActive";
    public const string AdminSetUserRoles = "AdminSetUserRoles";
    public const string AdminCreateRole = "AdminCreateRole";
    public const string AdminUpdateRole = "AdminUpdateRole";
    public const string AdminDeleteRole = "AdminDeleteRole";
}
