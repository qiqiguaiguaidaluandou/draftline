using Draftline.Core.Contracts;
using Draftline.Core.Enums;
using Draftline.Core.Models;
using Draftline.Core.Schemas;

namespace Draftline.Gateway.Stores;

/// <summary>
/// 服务器端字段提供者，基于内置 Schema。
/// </summary>
public sealed class ServerFieldProvider : IFieldProvider
{
    public IReadOnlyList<FieldDefinition> FieldsFor(FlowType flow) =>
        flow == FlowType.Pricing ? FieldSchemas.Pricing : FieldSchemas.DrawingSelection;
}
