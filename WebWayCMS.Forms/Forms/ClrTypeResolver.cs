namespace WebWayCMS.Forms;

/// <summary>
/// Resolves a stored CLR type name (a widget's or page type's configuration type, a page controller type, a form
/// component's value type): <see cref="Type.GetType(string, bool)"/> first, then every loaded assembly, so a type
/// in a host or satellite assembly resolves by its full name alone. The one resolver every registry uses.
/// </summary>
public static class ClrTypeResolver
{
    /// <summary>The type <paramref name="typeName"/> names, or null when it is empty, unknown or malformed.</summary>
    public static Type? Resolve(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        try
        {
            var type = Type.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
        catch
        {
            // A malformed name (e.g. an invalid assembly-qualified name) is just not a type.
            return null;
        }
    }
}
