namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Maps an admin content-type key to its Blazor admin list route. Centralises the route prefixes the
/// admin pages use (which intentionally differ from the still-live MVC <c>/admin/{contentType}</c>
/// routes during the transition) so version-history back/restore links resolve to the Blazor pages.
/// </summary>
public static class AdminRoutes
{
    public static string ListUrl(string contentType) => contentType switch
    {
        "contentblocks" => "/admin/blocks",
        "articles" => "/admin/article-lists",
        "contentzones" => "/admin/zones",
        "pages" => "/admin/site-pages",
        _ => "/admin",
    };
}
