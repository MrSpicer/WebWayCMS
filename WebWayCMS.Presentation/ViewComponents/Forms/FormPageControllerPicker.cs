using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Pages;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("PageControllerPicker", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.PageControllerPicker,
                  Category = "Selection", Order = 4,
                  DisplayName = "Page Controller Picker", Description = "Dropdown populated with registered page controllers.")]
public sealed class FormPageControllerPicker : FormFieldViewComponentBase
{
    private readonly IPageControllerRegistry _pageControllerRegistry;

    public FormPageControllerPicker(IPageControllerRegistry pageControllerRegistry)
    {
        _pageControllerRegistry = pageControllerRegistry ?? throw new ArgumentNullException(nameof(pageControllerRegistry));
    }

    public override IViewComponentResult Invoke(FormFieldContext field, string? viewName = null)
    {
        var name = viewName ?? (field.Mode == FormFieldMode.Read ? "Read" : "Write");
        if (field.Mode == FormFieldMode.Read)
            return View(name, field);

        var controllers = _pageControllerRegistry.GetAllControllers();

        Dictionary<string, string> options = new();
        foreach (var c in controllers)
        {
            var label = c.DisplayName;
            if (!string.IsNullOrEmpty(c.Description))
                label += " - " + c.Description;
            options[c.Name] = label;
        }

        var viewModel = new DropdownViewModel
        {
            Field = field,
            Options = options
        };

        return View(name, viewModel);
    }
}
