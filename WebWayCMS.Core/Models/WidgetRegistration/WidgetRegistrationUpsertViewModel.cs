using WebWayCMS.Attributes;

namespace WebWayCMS.Models.WidgetRegistration;

public sealed class WidgetRegistrationUpsertViewModel : BaseContentViewModel
{
    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 3,
        HelpText = "The ViewComponent identifier (e.g. 'ContentBlock').", FormComponent = "Text")]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Display Name", EditorType = EditorType.Text, IsRequired = true, Order = 4,
        HelpText = "Shown in the Add Widget dialog.", FormComponent = "Text")]
    public string DisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, Order = 5, FormComponent = "TextArea")]
    public string? Description { get; init; }

    [FormProperty(Label = "Category", EditorType = EditorType.Text, IsRequired = true, Order = 6,
        HelpText = "Grouping category (e.g. 'Content', 'Navigation').", FormComponent = "Text")]
    public string Category { get; init; } = "General";

    [FormProperty(Label = "Icon Class", EditorType = EditorType.Text, Order = 7,
        HelpText = "CSS icon class (e.g. 'fas fa-cube').", FormComponent = "Text")]
    public string? IconClass { get; init; }

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 8,
        HelpText = "Sort order within the category.", FormComponent = "Number")]
    public int Order { get; init; }

    [FormProperty(Label = "Configuration Type", EditorType = EditorType.Text, Order = 9,
        HelpText = "Fully qualified type name of the configuration class. Properties are auto-discovered on save.",
        FormComponent = "Text")]
    public string? ConfigurationTypeName { get; init; }

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 10,
        HelpText = "Whether this widget appears in the Add Widget dialog.", FormComponent = "Checkbox")]
    public bool IsActive { get; init; } = true;
}
