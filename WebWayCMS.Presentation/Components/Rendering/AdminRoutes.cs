namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Maps an admin content-type key to its Blazor admin list route. Centralises the route prefixes the
/// admin pages use so version-history back/restore links resolve to the correct Blazor list page.
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
