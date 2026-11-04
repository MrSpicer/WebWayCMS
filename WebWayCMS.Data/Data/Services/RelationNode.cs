namespace WebWayCMS.Data.Services;

/// <summary>A node's content type and current-draft title.</summary>
public sealed record RelationNode(Guid NodeId, string ContentTypeKey, string Title);
