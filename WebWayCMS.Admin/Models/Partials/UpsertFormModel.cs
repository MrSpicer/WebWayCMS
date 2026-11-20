using Microsoft.AspNetCore.Html;

namespace WebWayCMS.Admin.Partials;

/// <summary>
/// The admin edit form (<c>_UpsertForm</c>): validation summary, the <c>returnUrl</c> round trip, the model's
/// <c>&lt;form-fields&gt;</c>, and a footer with Save as Draft / Save and Publish / Unpublish
/// (<c>_UpsertPublishActions</c>) or a plain Save, then Cancel. It posts to <c>AdminContent/Edit</c>.
/// </summary>
public sealed record UpsertFormModel(string ContentType, object Model)
{
    public Guid? NodeId { get; init; }
    public bool IsPublished { get; init; }

    /// <summary>Draft/publish actions (versioned, publishable types) or a single Save button.</summary>
    public bool UsePublishActions { get; init; } = true;

    public string? FormId { get; init; }

    /// <summary>
    /// The return URL to round-trip. When null the form resolves it from the request
    /// (<c>AdminReturnUrl.Resolve</c>).
    /// </summary>
    public string? ReturnUrl { get; init; }

    /// <summary>Where Cancel goes without a return URL; defaults to the content type's index.</summary>
    public string? CancelUrl { get; init; }

    /// <summary>Content rendered in the card after the fields (e.g. a page type's configuration area).</summary>
    public Func<object?, IHtmlContent>? AfterFields { get; init; }
}
