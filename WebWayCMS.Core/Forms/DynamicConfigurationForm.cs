using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Static helper for materialising dynamic configuration form instances and returning a
/// <see cref="PartialViewResult"/> pointing at <c>_DynamicForm.cshtml</c>.
/// </summary>
public static class DynamicConfigurationForm
{
    private const string ViewPath = "~/Views/AdminShared/_DynamicForm.cshtml";

    /// <summary>
    /// Creates a configuration instance from the given type and JSON, handling edge cases
    /// (whitespace-padded JSON, literal <c>"null"</c>, malformed JSON, types with no
    /// parameterless constructor).
    /// </summary>
    public static object? Materialize(Type configurationType, string? valuesJson)
    {
        var trimmed = valuesJson?.Trim();
        var hasValue = !string.IsNullOrEmpty(trimmed) && trimmed != "{}";

        if (!hasValue)
            return Activator.CreateInstance(configurationType);

        try
        {
            var deserialized = JsonSerializer.Deserialize(
                trimmed!, configurationType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            if (deserialized != null)
                return deserialized;
        }
        catch (JsonException ex)
        {
            Serilog.Log.ForContext(typeof(DynamicConfigurationForm))
                .Warning(ex, "Failed to deserialize configuration JSON for type {Type}. Falling back to fresh instance.",
                    configurationType.Name);
        }

        try
        {
            return Activator.CreateInstance(configurationType);
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext(typeof(DynamicConfigurationForm))
                .Warning(ex, "Failed to create instance of configuration type {Type}.", configurationType.Name);
            return null;
        }
    }

    /// <summary>
    /// Returns a <see cref="PartialViewResult"/> that renders <c>_DynamicForm.cshtml</c>
    /// with the given model instance.
    /// </summary>
    /// <param name="instance">The configuration instance to render a form for.</param>
    /// <param name="componentName">
    /// The widget/component name the form is for, carried to the fields so ViewPicker properties
    /// without an explicit <c>ViewComponentName</c> discover views for the widget itself.
    /// </param>
    /// <param name="displayMode">
    /// The widget display mode the form is for; when set, only the fields tagged for that mode
    /// (<see cref="FormPropertyInfo.DisplayModes"/>) are rendered.
    /// </param>
    /// <param name="excludedProperties">Property names to leave out of the form entirely.</param>
    public static PartialViewResult Render(
        object? instance,
        string? componentName = null,
        WidgetDisplayMode? displayMode = null,
        IReadOnlyCollection<string>? excludedProperties = null)
    {
        var viewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = instance
        };

        if (!string.IsNullOrWhiteSpace(componentName))
            viewData["DynamicForm:ComponentName"] = componentName;

        if (displayMode.HasValue)
            viewData["DynamicForm:DisplayMode"] = displayMode.Value;

        if (excludedProperties is { Count: > 0 })
            viewData["DynamicForm:ExcludedProperties"] = excludedProperties;

        return new PartialViewResult
        {
            ViewName = ViewPath,
            ViewData = viewData
        };
    }
}
