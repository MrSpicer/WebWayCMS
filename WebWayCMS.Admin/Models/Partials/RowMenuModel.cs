using Microsoft.AspNetCore.Html;

namespace WebWayCMS.Admin.Partials;

/// <summary>
/// The "⋯" actions menu of an admin list row (<c>_RowMenu</c>): optional items, Version history, then a divider
/// and Delete (through the shared delete modal). <see cref="Id"/> is the row's id, used for the menu element id,
/// the history link and the delete URL.
/// </summary>
public sealed record RowMenuModel(string ContentType, string Id, string Title)
{
    public bool ShowHistory { get; init; } = true;

    /// <summary>The name the delete confirmation shows; defaults to <see cref="Title"/>.</summary>
    public string? DeleteName { get; init; }

    /// <summary>A local URL the delete redirects back to.</summary>
    public string? DeleteReturnUrl { get; init; }

    /// <summary>Descendants the delete takes with it, named in the confirmation (e.g. child pages).</summary>
    public int? DeleteDescendants { get; init; }
    public string? DeleteDescendantLabel { get; init; }

    /// <summary>Items before Version history.</summary>
    public Func<object?, IHtmlContent>? ItemsBefore { get; init; }

    /// <summary>Items after Version history.</summary>
    public Func<object?, IHtmlContent>? ItemsAfter { get; init; }

    /// <summary>Items after the divider, before Delete.</summary>
    public Func<object?, IHtmlContent>? DangerItems { get; init; }

    public bool HasItemsAboveDivider => ShowHistory || ItemsBefore != null || ItemsAfter != null;
}
