using WebWayCMS.Data.Models;

namespace WebWayCMS.Core.Tests;

// ─── a host-style content type pair: Faq → FaqTopic, declared with one attribute ───
public sealed class FixtureFaqDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();
    public string Answer { get; set; } = string.Empty;
}
