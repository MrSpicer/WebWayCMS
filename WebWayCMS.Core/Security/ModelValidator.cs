using System.ComponentModel.DataAnnotations;
using System.Reflection;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;

namespace WebWayCMS.Security;

/// <summary>
/// Validates an upsert view model at the save choke point, enforcing exactly the constraints that
/// <c>describe_content_type</c> advertises: DataAnnotations (via
/// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}, bool)"/>)
/// plus <see cref="FormPropertyAttribute"/> — <see cref="FormPropertyAttribute.IsRequired"/>, which
/// covers view models that carry no DataAnnotations (e.g. <c>ContentZoneItemUpsertViewModel</c>),
/// and the server-side bounds <see cref="FormPropertyAttribute.Min"/>, <see cref="FormPropertyAttribute.Max"/>,
/// <see cref="FormPropertyAttribute.MaxLength"/> and <see cref="FormPropertyAttribute.Pattern"/>, which
/// the client-side form cannot be trusted to enforce on MCP and content-seed writes.
/// </summary>
/// <remarks>
/// A stateless static entry point is used deliberately, mirroring <see cref="RichTextSanitizer"/>:
/// validation is a pure check shared by every content model's save path (both the admin UI and the
/// MCP tools), so threading it through model constructors would add churn without value.
/// </remarks>
public static class ModelValidator
{
    // Reflection results are stable per type; cache the FormProperty properties we check.
    private static readonly Dictionary<Type, (PropertyInfo[] Required, PropertyInfo[] Constrained)> FormPropertyCache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Returns a failure <see cref="AdminSaveResult"/> when <paramref name="viewModel"/> violates a
    /// required field or a <see cref="FormPropertyAttribute"/> bound, or <c>null</c> when it is valid.
    /// The failure's <c>ErrorField</c> is the PascalCase C# property name, matching the
    /// <c>ModelState</c> keys the admin controller binds with.
    /// </summary>
    public static AdminSaveResult? Validate(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(viewModel, new ValidationContext(viewModel), errors, validateAllProperties: true);

        // FormProperty.IsRequired-only fields carry no DataAnnotation, so TryValidateObject skips them.
        if (errors.Count == 0)
        {
            var (required, constrained) = GetFormProperties(viewModel.GetType());

            foreach (var prop in required)
            {
                if (IsEmpty(prop.GetValue(viewModel)))
                    errors.Add(new ValidationResult($"{LabelOf(prop)} is required.", new[] { prop.Name }));
            }

            foreach (var prop in constrained)
                CheckConstraints(prop, prop.GetValue(viewModel), errors);
        }

        if (errors.Count == 0)
            return null;

        var first = errors[0];
        return new AdminSaveResult(false, first.ErrorMessage, first.MemberNames.FirstOrDefault());
    }

    private static void CheckConstraints(PropertyInfo prop, object? value, List<ValidationResult> errors)
    {
        // Only called on properties that GetFormProperties already confirmed carry the attribute.
        var attr = prop.GetCustomAttribute<FormPropertyAttribute>()!;
        var messages = FormPropertyConstraints.Check(
            LabelOf(prop), value,
            double.IsNaN(attr.Min) ? null : attr.Min,
            double.IsNaN(attr.Max) ? null : attr.Max,
            attr.MaxLength >= 0 ? attr.MaxLength : null,
            attr.Pattern, attr.PatternErrorMessage);
        foreach (var message in messages)
            errors.Add(new ValidationResult(message, new[] { prop.Name }));
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        _ => false,
    };

    private static string LabelOf(PropertyInfo prop)
    {
        // Only called on properties that GetFormProperties already confirmed carry the attribute.
        var label = prop.GetCustomAttribute<FormPropertyAttribute>()!.Label;
        return string.IsNullOrEmpty(label) ? prop.Name : label;
    }

    private static (PropertyInfo[] Required, PropertyInfo[] Constrained) GetFormProperties(Type type)
    {
        lock (CacheLock)
        {
            if (FormPropertyCache.TryGetValue(type, out var cached))
                return cached;

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .Select(p => (Property: p, Attribute: p.GetCustomAttribute<FormPropertyAttribute>()))
                .Where(t => t.Attribute != null)
                .ToArray();

            // A field with a [Required] DataAnnotation is already covered by TryValidateObject.
            var required = props
                .Where(t => t.Attribute!.IsRequired && t.Property.GetCustomAttribute<RequiredAttribute>() == null)
                .Select(t => t.Property)
                .ToArray();

            var constrained = props
                .Where(t => !double.IsNaN(t.Attribute!.Min) || !double.IsNaN(t.Attribute.Max)
                    || t.Attribute.MaxLength >= 0
                    || !string.IsNullOrEmpty(t.Attribute.Pattern))
                .Select(t => t.Property)
                .ToArray();

            FormPropertyCache[type] = (required, constrained);
            return (required, constrained);
        }
    }
}
