namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Configuration for JSON content seeding, bound from the "ContentSeed" configuration section.
/// </summary>
public sealed class ContentSeedOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ContentSeed";

    /// <summary>Whether JSON content seeding runs at startup. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Directory (relative to the host's content root) scanned for <c>*.json</c> content seed files.
    /// A missing directory is not an error.
    /// </summary>
    public string Path { get; set; } = "contentseed";

    /// <summary>
    /// Case-insensitive suffix that marks an embedded resource as a content seed file.
    /// </summary>
    public string ResourceSuffix { get; set; } = ".contentseed.json";
}
