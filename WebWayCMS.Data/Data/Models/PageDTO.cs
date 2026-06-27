namespace WebWayCMS.Data.Models;

/// <summary>
/// Database entity representing a dynamic page with a custom route and controller configuration.
/// </summary>
public record PageDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    /// <summary>
    /// The URL route for this page (e.g., "/about", "/services/consulting").
    /// Must be unique, start with "/", and have no trailing slash.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// The registered page controller name (e.g., "AboutPage").
    /// </summary>
    public string ControllerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional Razor view name to render instead of the default Index.cshtml.
    /// </summary>
    public string? ViewName { get; set; }

    /// <summary>
    /// JSON-serialized configuration for the page controller.
    /// </summary>
    public string ConfigurationJson { get; set; } = "{}";
}