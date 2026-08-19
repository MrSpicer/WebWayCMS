namespace WebWayCMS.Services.ContentSeeding;

/// <summary>Enumerates the raw JSON content seed sources (embedded resources, disk files, etc.).</summary>
public interface IContentSeedSourceProvider
{
    IEnumerable<ContentSeedSource> GetSources();
}
