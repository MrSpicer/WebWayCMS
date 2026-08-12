using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

[CMSFormComponent("Dropdown", DataTypes = new[] { typeof(string) },
                  EditorType = EditorType.Dropdown, IsDefaultForType = true,
                  Category = "Selection", Order = 1,
                  DisplayName = "Dropdown", Description = "Dropdown select from predefined or enum options.")]
public sealed class FormDropdown : FormFieldViewComponentBase
{
    public override IViewComponentResult Invoke(FormFieldContext field, string? viewName = null)
    {
        var name = viewName ?? (field.Mode == FormFieldMode.Read ? "Read" : "Write");
        if (field.Mode == FormFieldMode.Read)
            return View(name, field);

        var options = field.DropdownOptions;

        // Populate from enum when DropdownOptions is empty and property type is an enum
        if (options.Count == 0)
        {
            var propType = field.Property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(propType) ?? propType;
            if (underlying.IsEnum)
            {
                options = new Dictionary<string, string>();
                foreach (var enumValue in Enum.GetValues(underlying))
                {
                    var enumName = Enum.GetName(underlying, enumValue);
                    if (enumName != null)
                    {
                        var intValue = Convert.ToInt32(enumValue);
                        options[intValue.ToString()] = enumName;
                    }
                }
            }
        }

        // Build a view model so the view has access to the populated options
        var viewModel = new DropdownViewModel
        {
            Field = field,
            Options = options
        };

        return View(name, viewModel);
    }
}

public sealed class DropdownViewModel
{
    public FormFieldContext Field { get; set; } = null!;
    public Dictionary<string, string> Options { get; set; } = new();
}
