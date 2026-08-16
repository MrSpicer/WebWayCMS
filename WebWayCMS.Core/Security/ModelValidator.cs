using System.ComponentModel.DataAnnotations;
using System.Reflection;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Security;

/// <summary>
/// Validates an upsert view model at the save choke point, enforcing exactly the required fields
/// that <c>describe_content_type</c> advertises: DataAnnotations (via
/// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}, bool)"/>)
/// plus <see cref="FormPropertyAttribute.IsRequired"/>, which covers view models that carry no
/// DataAnnotations (e.g. <c>ContentZoneItemUpsertViewModel</c>).
/// </summary>
/// <remarks>
/// A stateless static entry point is used deliberately, mirroring <see cref="RichTextSanitizer"/>:
/// validation is a pure check shared by every content model's save path (both the admin UI and the
/// MCP tools), so threading it through model constructors would add churn without value.
/// </remarks>
public static class ModelValidator
{
    // Reflection results are stable per type; cache the FormProperty-required properties we check.
    private static readonly Dictionary<Type, PropertyInfo[]> FormRequiredPropertyCache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Returns a failure <see cref="AdminSaveResult"/> when <paramref name="viewModel"/> violates a
    /// required field, or <c>null</c> when it is valid. The failure's <c>ErrorField</c> is the
    /// PascalCase C# property name, matching the <c>ModelState</c> keys the admin controller binds with.
    /// </summary>
    public static AdminSaveResult? Validate(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(viewModel, new ValidationContext(viewModel), errors, validateAllProperties: true);

        // FormProperty.IsRequired-only fields carry no DataAnnotation, so TryValidateObject skips them.
        if (errors.Count == 0)
        {
            foreach (var prop in GetFormRequiredProperties(viewModel.GetType()))
            {
                if (IsEmpty(prop.GetValue(viewModel)))
                    errors.Add(new ValidationResult($"{LabelOf(prop)} is required.", new[] { prop.Name }));
            }
        }

        if (errors.Count == 0)
            return null;

        var first = errors[0];
        return new AdminSaveResult(false, first.ErrorMessage, first.MemberNames.FirstOrDefault());
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        _ => false,
    };

    private static string LabelOf(PropertyInfo prop)
    {
        // Only called on properties that GetFormRequiredProperties already confirmed carry the attribute.
        var label = prop.GetCustomAttribute<FormPropertyAttribute>()!.Label;
        return string.IsNullOrEmpty(label) ? prop.Name : label;
    }

    private static PropertyInfo[] GetFormRequiredProperties(Type type)
    {
        lock (CacheLock)
        {
            if (FormRequiredPropertyCache.TryGetValue(type, out var cached))
                return cached;

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead
                    && p.GetCustomAttribute<FormPropertyAttribute>()?.IsRequired == true
                    && p.GetCustomAttribute<RequiredAttribute>() == null)
                .ToArray();

            FormRequiredPropertyCache[type] = props;
            return props;
        }
    }
}
