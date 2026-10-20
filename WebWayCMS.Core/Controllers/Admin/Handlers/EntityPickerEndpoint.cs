namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Maps a <c>[FormProperty(EntityType = ...)]</c> value to the admin API list URL that populates
/// an entity picker. Resolved server-side against the handler registry so host content types
/// (e.g. <c>faqs</c>) work without a client-side endpoint map.
/// </summary>
/// <remarks>
/// Resolution order: a handler whose <see cref="IAdminCrudHandler.ContentType"/> equals the entity
/// type, then its plural (<c>ContentBlock</c> → <c>contentblocks</c>), then a handler secondary list
/// key equal to the entity type or its plural (<c>ArticleList</c> → <c>articles/api/articlelists</c>).
/// </remarks>
public static class EntityPickerEndpoint
{
    private const string AdminPrefix = "/wadmin";

    /// <summary>
    /// Returns the list endpoint for <paramref name="entityType"/>, or <c>null</c> when no handler
    /// serves it (or there is no registry, as on a rendering-only host).
    /// </summary>
    public static string? Resolve(string? entityType, IAdminHandlerRegistry? registry)
    {
        if (string.IsNullOrWhiteSpace(entityType) || registry == null)
            return null;

        var plural = entityType + "s";

        var handler = registry.GetHandler(entityType) ?? registry.GetHandler(plural);
        if (handler != null)
            return $"{AdminPrefix}/{Uri.EscapeDataString(handler.ContentType)}/api/list";

        foreach (var candidate in registry.All)
        {
            if (!candidate.HasSecondaryApiList) continue;

            var key = candidate.SecondaryApiListKeys.FirstOrDefault(k =>
                string.Equals(k, entityType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(k, plural, StringComparison.OrdinalIgnoreCase));
            if (key != null)
                return $"{AdminPrefix}/{Uri.EscapeDataString(candidate.ContentType)}/api/{Uri.EscapeDataString(key)}";
        }

        return null;
    }
}
