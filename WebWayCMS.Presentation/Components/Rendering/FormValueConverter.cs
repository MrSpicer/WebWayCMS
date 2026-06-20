using WebWayCMS.Attributes;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Converts between a configuration model's typed property values and the string values used by the
/// interactive admin form inputs. Extracted from the form component so the (many) type branches are
/// unit-testable without rendering.
/// </summary>
public static class FormValueConverter
{
    /// <summary>Formats a model value for display in an input, mirroring the tag-helper's formatting.</summary>
    public static string ToInput(object? value, EditorType editorType)
    {
        if (value is null)
            return string.Empty;

        return editorType switch
        {
            EditorType.DateTime when value is DateTime dt => dt == default ? string.Empty : dt.ToString("yyyy-MM-ddTHH:mm"),
            EditorType.Date when value is DateTime d => d == default ? string.Empty : d.ToString("yyyy-MM-dd"),
            EditorType.Guid when value is Guid g => g == Guid.Empty ? string.Empty : g.ToString(),
            _ => $"{value}",
        };
    }

    /// <summary>Parses a raw input string into a value assignable to <paramref name="targetType"/>.</summary>
    public static object? FromInput(string? raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var nullable = !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;

        if (string.IsNullOrEmpty(raw))
            return nullable ? null : DefaultOf(underlying);

        if (underlying == typeof(string))
            return raw;
        if (underlying == typeof(bool))
            return raw is "true" or "True" or "on";
        if (underlying == typeof(int))
            return int.TryParse(raw, out var i) ? i : Fallback(nullable, underlying);
        if (underlying == typeof(double))
            return double.TryParse(raw, out var d) ? d : Fallback(nullable, underlying);
        if (underlying == typeof(Guid))
            return Guid.TryParse(raw, out var g) ? g : Fallback(nullable, underlying);
        if (underlying == typeof(DateTime))
            return DateTime.TryParse(raw, out var dt) ? dt : Fallback(nullable, underlying);

        return raw;
    }

    // Only ever called with a (non-nullable) value type, for which CreateInstance returns its default.
    private static object? DefaultOf(Type t) => Activator.CreateInstance(t);

    private static object? Fallback(bool nullable, Type underlying) => nullable ? null : DefaultOf(underlying);
}
