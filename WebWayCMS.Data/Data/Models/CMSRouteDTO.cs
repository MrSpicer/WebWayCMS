namespace WebWayCMS.Data.Models;

public record CMSRouteDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string Pattern { get; set; } = string.Empty;

    public string DefaultsJson { get; set; } = "{}";

    public string ConstraintsJson { get; set; } = "{}";

    public string DataTokensJson { get; set; } = "{}";

    public int Order { get; set; }

    public Guid? OwningContentMasterId { get; set; }

    public string? OwningContentType { get; set; }

    public bool IsReserved { get; set; }
}
