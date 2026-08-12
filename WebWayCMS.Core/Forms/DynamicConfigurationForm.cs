using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

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
    public static PartialViewResult Render(object? instance)
    {
        var viewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = instance
        };

        return new PartialViewResult
        {
            ViewName = ViewPath,
            ViewData = viewData
        };
    }
}
