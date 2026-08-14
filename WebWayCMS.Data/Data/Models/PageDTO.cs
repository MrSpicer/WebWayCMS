namespace WebWayCMS.Data.Models;

public record PageDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    /// <summary>The page controller (page type) selected for this page, e.g. "GenericPage".</summary>
    public string ControllerName { get; set; } = string.Empty;

    public string? ViewName { get; set; }

    public string ConfigurationJson { get; set; } = "{}";
}
