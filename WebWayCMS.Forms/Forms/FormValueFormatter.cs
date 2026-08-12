namespace WebWayCMS.Forms;

/// <summary>
/// Static formatting helpers for converting property values to their wire/display string
/// representation. Lifted from the tag helper's private <c>FormatValue</c> so components
/// can format values in their own views.
/// </summary>
public static class FormValueFormatter
{
    public static string Format(object? value)
    {
        if (value == null)
            return string.Empty;

        return value switch
        {
            DateTime dt => dt == DateTime.MinValue ? string.Empty : dt.ToString("yyyy-MM-ddTHH:mm"),
            DateTimeOffset dto => dto == DateTimeOffset.MinValue ? string.Empty : dto.ToString("yyyy-MM-ddTHH:mm"),
            DateOnly d => d == DateOnly.MinValue ? string.Empty : d.ToString("yyyy-MM-dd"),
            Guid g => g == Guid.Empty ? string.Empty : g.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    public static string FormatDateTime(DateTime? value)
    {
        if (value == null || value.Value == DateTime.MinValue)
            return string.Empty;
        return value.Value.ToString("yyyy-MM-ddTHH:mm");
    }

    public static string FormatDateTimeOffset(DateTimeOffset? value)
    {
        if (value == null || value.Value == DateTimeOffset.MinValue)
            return string.Empty;
        return value.Value.ToString("yyyy-MM-ddTHH:mm");
    }

    public static string FormatDateOnly(DateOnly? value)
    {
        if (value == null || value.Value == DateOnly.MinValue)
            return string.Empty;
        return value.Value.ToString("yyyy-MM-dd");
    }

    public static string FormatGuid(Guid? value)
    {
        if (value == null || value.Value == Guid.Empty)
            return string.Empty;
        return value.Value.ToString();
    }

    public static string FormatBool(bool? value)
        => value == true ? "true" : string.Empty;

    /// <summary>
    /// Formats a property value as a date string (yyyy-MM-dd), handling DateTime,
    /// DateTimeOffset, and DateOnly. MinValue values return an empty string.
    /// </summary>
    public static string FormatDateValue(object? value)
    {
        if (value == null)
            return string.Empty;

        return value switch
        {
            DateTime dt => dt == DateTime.MinValue ? string.Empty : dt.ToString("yyyy-MM-dd"),
            DateTimeOffset dto => dto == DateTimeOffset.MinValue ? string.Empty : dto.ToString("yyyy-MM-dd"),
            DateOnly d => d == DateOnly.MinValue ? string.Empty : d.ToString("yyyy-MM-dd"),
            _ => string.Empty
        };
    }
}
