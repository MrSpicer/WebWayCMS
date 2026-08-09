using WebWayCMS.Attributes;

namespace WebWayCMS.Models.PageControllerRegistration;

public sealed class PageControllerRegistrationUpsertViewModel : BaseContentViewModel
{
    [FormProperty(Label = "Controller Name", EditorType = EditorType.Text, IsRequired = true, Order = 3,
        HelpText = "The controller identifier (e.g. 'GenericPage').")]
    public string ControllerName { get; init; } = string.Empty;

    [FormProperty(Label = "Controller Type", EditorType = EditorType.Text, IsRequired = true, Order = 4,
        HelpText = "Fully qualified CLR type name of the controller class.")]
    public string ControllerTypeName { get; init; } = string.Empty;

    [FormProperty(Label = "Display Name", EditorType = EditorType.Text, IsRequired = true, Order = 5,
        HelpText = "Shown in the page type selection dropdown.")]
    public string DisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, Order = 6)]
    public string Description { get; init; } = string.Empty;

    [FormProperty(Label = "Category", EditorType = EditorType.Text, IsRequired = true, Order = 7,
        HelpText = "Grouping category (e.g. 'Content', 'Navigation').")]
    public string Category { get; init; } = "General";

    [FormProperty(Label = "Icon Class", EditorType = EditorType.Text, Order = 8,
        HelpText = "CSS icon class (e.g. 'fas fa-file').")]
    public string IconClass { get; init; } = string.Empty;

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 9,
        HelpText = "Sort order within the category.")]
    public int Order { get; init; }

    [FormProperty(Label = "Configuration Type", EditorType = EditorType.Text, Order = 10,
        HelpText = "Fully qualified type name of the configuration class. Properties are auto-discovered on save.")]
    public string? ConfigurationTypeName { get; init; }

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 11,
        HelpText = "Whether this page type appears in the page type selection dropdown.")]
    public bool IsActive { get; init; } = true;
}
