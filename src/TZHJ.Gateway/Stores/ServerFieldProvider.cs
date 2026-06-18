using TZHJ.Core.Contracts;
using TZHJ.Core.Enums;
using TZHJ.Core.Models;
using TZHJ.Core.Schemas;

namespace TZHJ.Gateway.Stores;

/// <summary>
/// 服务器端字段提供者，基于内置 Schema。
/// </summary>
public sealed class ServerFieldProvider : IFieldProvider
{
    public IReadOnlyList<FieldDefinition> FieldsFor(FlowType flow) =>
        flow == FlowType.Pricing ? FieldSchemas.Pricing : FieldSchemas.DrawingSelection;
}
