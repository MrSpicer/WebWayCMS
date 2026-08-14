namespace WebWayCMS.Data.Models;

public record ArticleDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    public string Body { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    /// <summary>FK to <see cref="ContentNode.Id"/> — a real foreign key, not a soft reference.</summary>
    public Guid ArticleListNodeId { get; set; }
}
