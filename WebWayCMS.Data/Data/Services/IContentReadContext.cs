namespace WebWayCMS.Data.Services;

public enum ContentReadMode
{
    Published = 0,
    Draft = 1
}

/// <summary>
/// Describes which versions a read should select. Registered per deployment mode: a rendering-only
/// host resolves a hard-coded <see cref="ContentReadMode.Published"/> context, while an admin host
/// resolves a preview-aware context that can serve drafts to authenticated editors.
/// </summary>
public interface IContentReadContext
{
    ContentReadMode Mode { get; }
    string Culture { get; }
    string Segment { get; }
}
