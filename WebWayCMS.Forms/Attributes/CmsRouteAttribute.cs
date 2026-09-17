namespace WebWayCMS.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CmsRouteAttribute : Attribute
{
    public string Pattern { get; }

    public int Order { get; set; }

    public string Action { get; set; } = "Index";

    /// <summary>
    /// Link text for this route in navigation widgets. Routes with no navigation name are not shown.
    /// On a pattern that was already seeded, this fills a blank name on the existing row but never
    /// overwrites one an admin set in /wadmin/cmsroutes.
    /// </summary>
    public string? NavigationName { get; set; }

    public string? Defaults { get; set; }

    public string? Constraints { get; set; }

    public string? DataTokens { get; set; }

    /// <summary>
    /// Marks the seeded row as reserved: it occupies the pattern so an editor cannot create a page
    /// that would be shadowed by this controller, but it is never matched for dispatch itself.
    /// </summary>
    public bool IsReserved { get; set; }

    public CmsRouteAttribute(string pattern)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }
}
