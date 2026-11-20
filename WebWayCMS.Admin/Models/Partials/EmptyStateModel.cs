namespace WebWayCMS.Admin.Partials;

/// <summary>The empty-list card of an admin index (<c>_EmptyState</c>), with an optional subtitle and create button.</summary>
public sealed record EmptyStateModel(string Icon, string Title)
{
    public string? Subtitle { get; init; }
    public string? CreateUrl { get; init; }
    public string? CreateLabel { get; init; }
}
