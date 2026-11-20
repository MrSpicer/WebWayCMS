using Microsoft.AspNetCore.Html;

namespace WebWayCMS.Admin.Partials;

/// <summary>
/// The one-row admin page header (<c>_AdminPageHeader</c>): ancestor crumbs, the title, an optional node id
/// subtitle, an optional primary "New …" button, and optional extra actions / content below the row.
/// </summary>
public sealed record AdminPageHeaderModel(string Title)
{
    public IReadOnlyList<AdminCrumb> Crumbs { get; init; } = [];

    /// <summary>Shown after the title as a code subtitle (edit screens show the item's node id).</summary>
    public Guid? NodeId { get; init; }

    /// <summary>A plain-text subtitle after the title (e.g. a description).</summary>
    public string? Subtitle { get; init; }

    /// <summary>A primary "New …" button, rendered when both the URL and the label are set.</summary>
    public string? NewUrl { get; init; }
    public string? NewLabel { get; init; }

    /// <summary><c>container is-fluid</c> (list screens) or a plain <c>container</c> (edit screens).</summary>
    public bool Fluid { get; init; } = true;

    /// <summary>Extra buttons rendered at the end of the title row.</summary>
    public Func<object?, IHtmlContent>? Actions { get; init; }

    /// <summary>Content rendered under the title row (e.g. a filter form).</summary>
    public Func<object?, IHtmlContent>? Below { get; init; }
}
