namespace WebWayCMS.Admin.Partials;

/// <summary>
/// The Publish / Unpublish button of an admin list row (<c>_RowPublishToggle</c>): Publish while the item is
/// not live or has pending changes, otherwise Unpublish.
/// </summary>
public sealed record RowPublishToggleModel(string ContentType, Guid NodeId, bool IsPublished)
{
    public bool HasPendingChanges { get; init; }

    /// <summary>A local URL the action redirects back to.</summary>
    public string? ReturnUrl { get; init; }

    public bool OffersPublish => !IsPublished || HasPendingChanges;
}
