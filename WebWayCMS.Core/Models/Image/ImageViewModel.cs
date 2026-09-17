using WebWayCMS.Media;

namespace WebWayCMS.Models.Image;

/// <summary>
/// Render model for an image. <see cref="Width"/> and <see cref="Height"/> come from the media
/// catalog so views can emit explicit dimensions and avoid layout shift.
/// </summary>
public sealed class ImageViewModel : BaseContentViewModel
{
    public string BlobHash { get; init; } = string.Empty;
    public string AltText { get; init; } = string.Empty;
    public string? Caption { get; init; }

    public string ContentType { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Optional class applied by the zone widget at render time.</summary>
    public string? CssClass { get; set; }

    /// <summary>Whether the zone widget should render the caption.</summary>
    public bool ShowCaption { get; set; }

    /// <summary>The public, immutably cacheable URL for the referenced blob.</summary>
    public string Url => MediaUrl.For(BlobHash, OriginalFileName);
}
