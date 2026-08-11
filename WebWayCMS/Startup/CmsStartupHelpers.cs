namespace WebWayCMS.Startup;

internal static class CmsStartupHelpers
{
    internal static string GetControllerName(Type type)
    {
        const string suffix = "Controller";
        var name = type.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }

    internal static bool IsSkipped(string envVar)
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(envVar),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
