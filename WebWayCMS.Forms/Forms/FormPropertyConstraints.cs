using System.Text.RegularExpressions;

namespace WebWayCMS.Forms;

/// <summary>
/// The server-side checks of a field's <c>[FormProperty]</c> bounds (min, max, max length, pattern) — one
/// implementation shared by the upsert-model validator, the widget registry and the page-controller registry,
/// so their rules and messages cannot drift apart. Required is checked by each caller (what counts as empty
/// differs between them).
/// </summary>
/// <remarks>
/// <see cref="Pattern"/> follows the HTML <c>pattern</c> attribute the form renders: an empty value is not
/// checked (that is what required is for) and the pattern must match the whole value, as if wrapped in
/// <c>^(?:…)$</c>. A match is bounded by <see cref="PatternTimeout"/>; a pattern that times out counts as a
/// mismatch.
/// </remarks>
public static class FormPropertyConstraints
{
    /// <summary>How long one pattern match may run before the value is treated as not matching.</summary>
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The constraint errors of <paramref name="value"/> against <paramref name="property"/>.</summary>
    public static IReadOnlyList<string> Check(FormPropertyInfo property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        return Check(property.Label, value, property.Min, property.Max, property.MaxLength,
            property.Pattern, property.PatternErrorMessage);
    }

    /// <summary>
    /// The constraint errors of <paramref name="value"/>, labelled with <paramref name="label"/>. A null
    /// value has none; min/max apply to anything that parses as a number, max length and pattern to strings.
    /// </summary>
    public static IReadOnlyList<string> Check(
        string label, object? value, double? min, double? max, int? maxLength, string? pattern, string? patternErrorMessage)
    {
        var errors = new List<string>();
        if (value == null)
            return errors;

        if ((min.HasValue || max.HasValue) && double.TryParse(value.ToString(), out var number))
        {
            if (min.HasValue && number < min.Value)
                errors.Add($"{label} must be at least {min.Value}.");
            if (max.HasValue && number > max.Value)
                errors.Add($"{label} must be at most {max.Value}.");
        }

        if (value is string text)
        {
            if (maxLength.HasValue && text.Length > maxLength.Value)
                errors.Add($"{label} must not exceed {maxLength.Value} characters.");

            if (!string.IsNullOrEmpty(pattern) && !MatchesPattern(text, pattern))
                errors.Add(string.IsNullOrEmpty(patternErrorMessage) ? $"{label} has an invalid format." : patternErrorMessage);
        }

        return errors;
    }

    /// <summary>
    /// True when <paramref name="text"/> satisfies <paramref name="pattern"/> with HTML <c>pattern</c>
    /// semantics: empty passes, otherwise the whole value must match within <see cref="PatternTimeout"/>.
    /// </summary>
    public static bool MatchesPattern(string text, string pattern)
    {
        if (text.Length == 0)
            return true;

        try
        {
            return Regex.IsMatch(text, $"^(?:{pattern})$", RegexOptions.None, PatternTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
