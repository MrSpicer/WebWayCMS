using WebWayCMS.Data.Models;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Computes the document title for a CMS page. Extracted from the layout markup so the
/// null/empty branches can be unit-tested without rendering a component.
/// </summary>
public static class CmsPageTitle
{
    private const string Suffix = " - WebWayCMS";
    private const string Fallback = "Page" + Suffix;

    /// <summary>
    /// Returns "{page title} - WebWayCMS", or "Page - WebWayCMS" when the page or its title is missing.
    /// </summary>
    public static string ForPage(PageDTO? page)
    {
        var title = page?.ContentMeta?.Title;
        return string.IsNullOrWhiteSpace(title) ? Fallback : title + Suffix;
    }
}
