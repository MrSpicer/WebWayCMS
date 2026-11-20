using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Services;

namespace WebWayCMS.Models.ContentZone;

/// <summary>Exposes the content zone component registry as admin JSON endpoints.</summary>
internal sealed class ContentZoneRegistryHandler : IAdminRegistryHandler
{
    private readonly IWidgetRegistry _registry;
    private readonly IViewDiscoveryService _viewDiscoveryService;
    private readonly Serilog.ILogger _logger =
        Serilog.Log.ForContext<ContentZoneRegistryHandler>();

    public ContentZoneRegistryHandler(
        IWidgetRegistry registry,
        IViewDiscoveryService viewDiscoveryService)
    {
        _registry = registry;
        _viewDiscoveryService = viewDiscoveryService;
    }

    public IActionResult GetAll()
    {
        var components = _registry.GetAllComponents().Select(c => new
        {
            name = c.Name,
            displayName = c.DisplayName,
            description = c.Description,
            category = c.Category,
            supportsList = c.SupportsList
        }).ToList();

        return new JsonResult(components);
    }

    public IActionResult GetProperties(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Component name is required." });

        var component = _registry.GetByName(name);
        if (component == null)
            return new NotFoundObjectResult(new { error = $"Component '{name}' not found." });

        var properties = component.Properties.Select(p =>
        {
            Dictionary<string, string> dropdownOptions = p.DropdownOptions;

            if (p.EditorType == EditorType.ViewPicker)
            {
                // The picker lists only this property's render mode's views. The component name
                // comes from the attribute when set, otherwise from the registered widget.
                var viewComponentName = string.IsNullOrWhiteSpace(p.ViewComponentName) ? component.Name : p.ViewComponentName;
                var views = _viewDiscoveryService.GetAvailableViews(viewComponentName, p.RenderMode);
                if (views.Any())
                    dropdownOptions = views.ToDictionary(v => v, v => v);
                else
                {
                    _logger.Warning("No {Mode} views found for ViewComponent '{ComponentName}'", p.RenderMode, viewComponentName);
                    dropdownOptions = new Dictionary<string, string>();
                }
            }

            return new
            {
                name = p.Name,
                label = p.Label,
                helpText = p.HelpText,
                placeholder = p.Placeholder,
                editorType = p.EditorType.ToString().ToLowerInvariant(),
                renderMode = p.RenderMode.ToString().ToLowerInvariant(),
                displayModes = p.DisplayModes.ToString().ToLowerInvariant(),
                isRequired = p.IsRequired,
                defaultValue = p.DefaultValue,
                order = p.Order,
                group = p.Group,
                entityType = p.EntityType,
                dropdownOptions,
                viewComponentName = p.ViewComponentName,
                min = p.Min,
                max = p.Max,
                maxLength = p.MaxLength
            };
        }).OrderBy(p => p.order).ToList();

        return new JsonResult(new
        {
            componentName = component.Name,
            displayName = component.DisplayName,
            category = component.Category,
            supportsList = component.SupportsList,
            properties
        });
    }

    public IActionResult GetForm(string name, string? valuesJson) => GetForm(name, valuesJson, null);

    public IActionResult GetForm(string name, string? valuesJson, WidgetDisplayMode? displayMode)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new { error = "Component name is required." });

        var component = _registry.GetByName(name);
        if (component == null)
            return new NotFoundObjectResult(new { error = $"Component '{name}' not found." });

        object? instance = null;
        if (!string.IsNullOrWhiteSpace(component.ConfigurationTypeName))
        {
            var configType = ClrTypeResolver.Resolve(component.ConfigurationTypeName);
            if (configType != null)
                instance = WebWayCMS.Forms.DynamicConfigurationForm.Materialize(configType, valuesJson);
        }

        return WebWayCMS.Forms.DynamicConfigurationForm.Render(instance, name, displayMode);
    }
}
