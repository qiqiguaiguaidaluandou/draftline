using TZHJ.Core.Enums;
using TZHJ.Core.Models;

namespace TZHJ.Core.Contracts;

/// <summary>
/// 提供某流程的字段集。默认实现返回内置 schema；登录后由 IConfigGateway 下发的 ClientConfig 覆盖。
/// xlsx 读写、网格列、回传字段都以此为准——"加字段不改代码"。
/// </summary>
public interface IFieldProvider
{
    IReadOnlyList<FieldDefinition> FieldsFor(FlowType flow);
}
