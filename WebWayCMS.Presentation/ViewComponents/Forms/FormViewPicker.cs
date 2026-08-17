using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("ViewPicker", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.ViewPicker,
                  Category = "Selection", Order = 3,
                  DisplayName = "View Picker", Description = "Dropdown populated with available views for a ViewComponent.")]
public sealed class FormViewPicker : FormFieldViewComponentBase
{
    private readonly IViewDiscoveryService _viewDiscoveryService;

    public FormViewPicker(IViewDiscoveryService viewDiscoveryService)
    {
        _viewDiscoveryService = viewDiscoveryService ?? throw new ArgumentNullException(nameof(viewDiscoveryService));
    }

    public override IViewComponentResult Invoke(FormFieldContext field, string? viewName = null)
    {
        var name = viewName ?? (field.Mode == FormFieldMode.Read ? "Read" : "Write");
        if (field.Mode == FormFieldMode.Read)
            return View(name, field);

        Dictionary<string, string> options = new();
        if (!string.IsNullOrWhiteSpace(field.ViewComponentName))
        {
            var views = _viewDiscoveryService.GetAvailableViews(field.ViewComponentName);
            if (views.Any())
                options = views.ToDictionary(v => v, v => v);
        }

        // Keep a stored value selectable even when it is not among the discovered views, so a view
        // whose file was renamed or lives outside the scanned locations does not silently vanish on
        // the next save (and, on an edit load, the server-rendered select actually selects it).
        if (!string.IsNullOrWhiteSpace(field.StringValue) && !options.ContainsKey(field.StringValue))
            options[field.StringValue] = field.StringValue;

        var viewModel = new DropdownViewModel
        {
            Field = field,
            Options = options
        };

        return View(name, viewModel);
    }
}
