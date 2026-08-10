namespace WebWayCMS.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CmsRouteAttribute : Attribute
{
    public string Pattern { get; }

    public int Order { get; set; }

    public string Action { get; set; } = "Index";

    public string? Defaults { get; set; }

    public string? Constraints { get; set; }

    public string? DataTokens { get; set; }

    public CmsRouteAttribute(string pattern)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
    }
}
