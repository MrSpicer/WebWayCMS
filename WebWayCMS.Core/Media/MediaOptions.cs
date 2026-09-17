namespace WebWayCMS.Media;

/// <summary>
/// Media upload configuration, bound from the "Media" configuration section.
/// </summary>
public sealed class MediaOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Media";

    /// <summary>Default upload ceiling: 10 MB.</summary>
    public const long DefaultMaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Largest accepted upload, in bytes. Defaults to 10 MB. Note that the endpoint also needs a
    /// matching framework-level limit — without one, uploads silently inherit Kestrel's 30 MB default.
    /// </summary>
    public long MaxUploadBytes { get; set; } = DefaultMaxUploadBytes;

    /// <summary>
    /// Content types accepted on upload. Empty means "every format the validator can verify".
    /// Values are matched against the type sniffed from the file's bytes, never against the
    /// client-declared header.
    /// </summary>
    public List<string> AllowedContentTypes { get; set; } = new();
}
