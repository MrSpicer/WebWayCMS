using WebWayCMS.Attributes;

namespace WebWayCMS.Models.WidgetRegistration;

public sealed class WidgetRegistrationUpsertViewModel : BaseContentViewModel
{
    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 3,
        HelpText = "The ViewComponent identifier (e.g. 'ContentBlock').")]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Display Name", EditorType = EditorType.Text, IsRequired = true, Order = 4,
        HelpText = "Shown in the Add Widget dialog.")]
    public string DisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, Order = 5)]
    public string Description { get; init; } = string.Empty;

    [FormProperty(Label = "Category", EditorType = EditorType.Text, IsRequired = true, Order = 6,
        HelpText = "Grouping category (e.g. 'Content', 'Navigation').")]
    public string Category { get; init; } = "General";

    [FormProperty(Label = "Icon Class", EditorType = EditorType.Text, Order = 7,
        HelpText = "CSS icon class (e.g. 'fas fa-cube').")]
    public string IconClass { get; init; } = string.Empty;

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 8,
        HelpText = "Sort order within the category.")]
    public int Order { get; init; }

    [FormProperty(Label = "Configuration Type", EditorType = EditorType.Text, Order = 9,
        HelpText = "Fully qualified type name of the configuration class. Properties are auto-discovered on save.")]
    public string? ConfigurationTypeName { get; init; }

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 10,
        HelpText = "Whether this widget appears in the Add Widget dialog.")]
    public bool IsActive { get; init; } = true;
}
