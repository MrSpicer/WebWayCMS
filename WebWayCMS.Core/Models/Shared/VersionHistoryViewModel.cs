namespace WebWayCMS.Models.Shared;

public sealed class VersionHistoryViewModel
{
    public string ContentType { get; init; } = string.Empty;
    public Guid NodeId { get; init; }
    public string ItemTitle { get; init; } = string.Empty;
    public string BackUrl { get; set; } = string.Empty;

    /// <summary>
    /// A local URL the history page was opened from (e.g. the page zone editor). It replaces
    /// <see cref="BackUrl"/> and is carried through the page's restore/delete forms.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>The current draft's version number (a restore's expected version), when known.</summary>
    public int? CurrentVersionNumber { get; init; }

    public string? ParentKey { get; init; }
    public string? ChildType { get; init; }

    /// <summary>
    /// True when the rows include other items' versions (a container's page lists the widgets inside it):
    /// the page shows a Widget column, and each row's restore expects its own item's current version.
    /// </summary>
    public bool IsCombined { get; init; }

    public List<VersionItemViewModel> Versions { get; init; } = new();
}
