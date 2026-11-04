namespace WebWayCMS.Mcp;

/// <summary>
/// The relation a field declares (<c>[ContentRelation]</c>): its kind (<c>containment</c>,
/// <c>reference</c> or <c>owned</c>), the target content type, whether it holds many ids, and whether
/// a target is required. The field's value is the target's node id (or an array of them).
/// </summary>
public sealed record McpRelationDescription(string Kind, string Target, bool Many, bool Required);
