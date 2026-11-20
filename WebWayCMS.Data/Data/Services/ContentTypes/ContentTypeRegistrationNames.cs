using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.ContentTypes;

/// <summary>
/// The names admins gave content types, read from the published registrations
/// (<see cref="IContentTypeRegistrationService.GetPublishedByKeyAsync"/>), with the caller's own name as the
/// fallback when a type has no published registration or a blank name.
/// </summary>
public static class ContentTypeRegistrationNames
{
    /// <summary>The singular name of <paramref name="key"/>, else <paramref name="fallback"/>.</summary>
    public static string DisplayNameOr(
        this IReadOnlyDictionary<string, ContentTypeRegistrationDTO> published, string? key, string fallback)
        => Lookup(published, key)?.DisplayName is { } name && !string.IsNullOrWhiteSpace(name) ? name : fallback;

    /// <summary>The plural name of <paramref name="key"/>, else <paramref name="fallback"/>.</summary>
    public static string PluralDisplayNameOr(
        this IReadOnlyDictionary<string, ContentTypeRegistrationDTO> published, string? key, string fallback)
        => Lookup(published, key)?.PluralDisplayName is { } name && !string.IsNullOrWhiteSpace(name) ? name : fallback;

    private static ContentTypeRegistrationDTO? Lookup(IReadOnlyDictionary<string, ContentTypeRegistrationDTO> published, string? key)
    {
        ArgumentNullException.ThrowIfNull(published);
        return key != null && published.TryGetValue(key, out var registration) ? registration : null;
    }
}
