using WebWayCMS.Attributes;

namespace WebWayCMS.Models.PageControllerRegistration;

public sealed class PageControllerRegistrationUpsertViewModel : BaseContentViewModel
{
    [FormProperty(Label = "Controller Name", EditorType = EditorType.Text, IsRequired = true, Order = 3,
        HelpText = "The controller identifier (e.g. 'GenericPage').", FormComponent = "Text")]
    public string ControllerName { get; init; } = string.Empty;

    [FormProperty(Label = "Controller Type", EditorType = EditorType.Text, IsRequired = true, Order = 4,
        HelpText = "Fully qualified CLR type name of the controller class.", FormComponent = "Text")]
    public string ControllerTypeName { get; init; } = string.Empty;

    [FormProperty(Label = "Display Name", EditorType = EditorType.Text, IsRequired = true, Order = 5,
        HelpText = "Shown in the page type selection dropdown.", FormComponent = "Text")]
    public string DisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, Order = 6, FormComponent = "TextArea")]
    public string? Description { get; init; }

    [FormProperty(Label = "Category", EditorType = EditorType.Text, IsRequired = true, Order = 7,
        HelpText = "Grouping category (e.g. 'Content', 'Navigation').", FormComponent = "Text")]
    public string Category { get; init; } = "General";

    [FormProperty(Label = "Icon Class", EditorType = EditorType.Text, Order = 8,
        HelpText = "CSS icon class (e.g. 'fas fa-file').", FormComponent = "Text")]
    public string? IconClass { get; init; }

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 9,
        HelpText = "Sort order within the category.", FormComponent = "Number")]
    public int Order { get; init; }

    [FormProperty(Label = "Configuration Type", EditorType = EditorType.Text, Order = 10,
        HelpText = "Fully qualified type name of the configuration class. Properties are auto-discovered on save.",
        FormComponent = "Text")]
    public string? ConfigurationTypeName { get; init; }

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 11,
        HelpText = "Whether this page type appears in the page type selection dropdown.", FormComponent = "Checkbox")]
    public bool IsActive { get; init; } = true;
}
