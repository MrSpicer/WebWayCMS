using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Static utility for building <see cref="FormPropertyInfo"/> metadata from model types
/// decorated with <see cref="FormPropertyAttribute"/>.
/// </summary>
public static class FormPropertyBuilder
{
    public static List<FormPropertyInfo> BuildPropertyInfos(Type modelType)
    {
        var properties = new List<FormPropertyInfo>();

        foreach (var prop in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite)
                continue;

            var attr = prop.GetCustomAttribute<FormPropertyAttribute>();
            var requiredAttr = prop.GetCustomAttribute<RequiredAttribute>();
            var rangeAttr = prop.GetCustomAttribute<RangeAttribute>();
            var stringLengthAttr = prop.GetCustomAttribute<StringLengthAttribute>();
            var regexAttr = prop.GetCustomAttribute<RegularExpressionAttribute>();

            var propInfo = new FormPropertyInfo
            {
                Name = prop.Name,
                Label = attr?.Label ?? InsertSpaces(prop.Name),
                HelpText = attr?.HelpText ?? string.Empty,
                Placeholder = attr?.Placeholder ?? string.Empty,
                EditorType = attr?.EditorType ?? InferEditorType(prop.PropertyType),
                PropertyType = prop.PropertyType,
                Order = attr?.Order ?? 0,
                CssClass = attr?.CssClass ?? string.Empty,
                GroupWithNext = attr?.GroupWithNext ?? false,
                Group = attr?.Group ?? string.Empty,
                IsRequired = attr?.IsRequired == true || requiredAttr != null,
                EntityType = attr?.EntityType ?? string.Empty,
                ViewComponentName = attr?.ViewComponentName ?? string.Empty,
                Min = GetMinValue(attr, rangeAttr),
                Max = GetMaxValue(attr, rangeAttr),
                MaxLength = GetMaxLengthValue(attr, stringLengthAttr),
                Pattern = attr?.Pattern ?? regexAttr?.Pattern ?? string.Empty,
                PatternErrorMessage = attr?.PatternErrorMessage ?? regexAttr?.ErrorMessage ?? string.Empty,
                DefaultValue = GetDefaultValue(prop.PropertyType)
            };

            // Parse dropdown options
            if (!string.IsNullOrEmpty(attr?.DropdownOptions))
            {
                propInfo.DropdownOptions = ParseDropdownOptions(attr.DropdownOptions);
            }

            properties.Add(propInfo);
        }

        // Sort by Order, then by name
        properties.Sort((a, b) =>
        {
            var orderCompare = a.Order.CompareTo(b.Order);
            return orderCompare != 0 ? orderCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return properties;
    }

    public static EditorType InferEditorType(Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying == typeof(Guid))
            return EditorType.Guid;
        if (underlying == typeof(bool))
            return EditorType.Checkbox;
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) ||
            underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
            return EditorType.Number;
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
            return EditorType.DateTime;
        if (underlying == typeof(DateOnly))
            return EditorType.Date;
        if (underlying.IsEnum)
            return EditorType.Dropdown;

        return EditorType.Text;
    }

    public static object? GetDefaultValue(Type propertyType)
    {
        if (propertyType.IsValueType)
            return Activator.CreateInstance(propertyType);
        return null;
    }

    public static double? GetMinValue(FormPropertyAttribute? attr, RangeAttribute? rangeAttr)
    {
        if (attr != null && !double.IsNaN(attr.Min))
            return attr.Min;
        if (rangeAttr?.Minimum != null && double.TryParse(rangeAttr.Minimum.ToString(), out var min))
            return min;
        return null;
    }

    public static double? GetMaxValue(FormPropertyAttribute? attr, RangeAttribute? rangeAttr)
    {
        if (attr != null && !double.IsNaN(attr.Max))
            return attr.Max;
        if (rangeAttr?.Maximum != null && double.TryParse(rangeAttr.Maximum.ToString(), out var max))
            return max;
        return null;
    }

    public static int? GetMaxLengthValue(FormPropertyAttribute? attr, StringLengthAttribute? stringLengthAttr)
    {
        if (attr != null && attr.MaxLength >= 0)
            return attr.MaxLength;
        return stringLengthAttr?.MaximumLength;
    }

    public static Dictionary<string, string> ParseDropdownOptions(string options)
    {
        var result = new Dictionary<string, string>();
        var pairs = options.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split(':', 2);
            var value = parts[0].Trim();
            var label = parts.Length > 1 ? parts[1].Trim() : value;
            result[value] = label;
        }

        return result;
    }

    public static string InsertSpaces(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Insert space before each uppercase letter (except the first)
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}
