namespace WebWayCMS.Data.Models;

/// <summary>
/// The registry row of one content type (a key registered with a content store, CMS or host). Metadata and
/// discoverability only: a registration never changes whether its content type works. The key and CLR type are
/// code-owned (synced on every boot); the names, description, category, icon and order are admin-edited.
/// </summary>
public record ContentTypeRegistrationDTO : IVersionedContent, IRegistrationCatalog
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    /// <summary>The content type key (<see cref="ContentNode.ContentTypeKey"/>), e.g. "pages". Code-owned.</summary>
    public string ContentTypeKey { get; set; } = string.Empty;

    /// <summary>The full name of the content type's CLR type. Code-owned, informational.</summary>
    public string ClrTypeName { get; set; } = string.Empty;

    /// <summary>The singular name, e.g. "Article List".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The plural name, e.g. "Article Lists" (the admin navbar and children panels).</summary>
    public string PluralDisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = string.Empty;
    public int Order { get; set; }
}
