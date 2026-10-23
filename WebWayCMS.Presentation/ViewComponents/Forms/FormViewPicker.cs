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

    public override IViewComponentResult Invoke(FormFieldContext field)
    {
        if (field.Mode == FormFieldMode.Read)
            return FieldView(field, field);

        // The picker lists only the field's render mode's views (e.g. view.* on the content config
        // form, edit.* in the metadata editor). The component name comes from the field's
        // ViewComponentName when set, else the widget name supplied by the form context.
        var componentName = !string.IsNullOrWhiteSpace(field.ViewComponentName)
            ? field.ViewComponentName
            : field.ComponentName;

        // The empty "-- Default --" option already selects the mode's default view, so the
        // "default" variant itself is not listed again.
        var hasViews = false;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            var views = _viewDiscoveryService.GetAvailableViews(componentName, field.RenderMode);
            hasViews = views.Count > 0;
            foreach (var view in views.Where(v => !string.Equals(v, "default", StringComparison.OrdinalIgnoreCase)))
                options[view] = view;
        }

        // Keep a stored value selectable even when it is not among the discovered views, so a view
        // whose file was renamed or lives outside the scanned locations does not silently vanish on
        // the next save (and, on an edit load, the server-rendered select actually selects it).
        if (!string.IsNullOrWhiteSpace(field.StringValue) && !options.ContainsKey(field.StringValue))
            options[field.StringValue] = field.StringValue;

        // Picker visibility is inferred from files: a mode with neither views nor a stored value
        // renders nothing, so the content form never shows e.g. an edit-mode picker.
        if (!hasViews && options.Count == 0)
            return Content(string.Empty);

        var viewModel = new DropdownViewModel
        {
            Field = field,
            Options = options
        };

        return FieldView(field, viewModel);
    }
}
