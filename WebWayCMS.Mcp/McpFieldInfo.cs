namespace WebWayCMS.Mcp;

/// <summary>Describes a single editable field of a content type's upsert form.</summary>
public sealed record McpFieldInfo(
    string Name,
    string Label,
    string EditorType,
    bool Required,
    string? HelpText,
    int Order,
    McpRelationDescription? Relation = null);
