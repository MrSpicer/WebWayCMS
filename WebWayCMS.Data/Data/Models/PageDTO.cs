namespace WebWayCMS.Data.Models;

public record PageDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string? ViewName { get; set; }

    public string ConfigurationJson { get; set; } = "{}";
}
