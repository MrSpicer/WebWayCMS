namespace WebWayCMS.Data.Models;

public record ContentBlockDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string Content { get; set; } = string.Empty;
}
