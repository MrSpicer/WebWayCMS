using WebWayCMS.Attributes;

namespace WebWayCMS.Models.FormComponentRegistration;

public sealed class FormComponentRegistrationUpsertViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public Guid? Id { get; set; }

    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 3,
        HelpText = "The registry key (e.g. 'RichText').", FormComponent = "Text")]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "View Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 4,
        HelpText = "The MVC ViewComponent name (e.g. 'FormRichText').", FormComponent = "Text")]
    public string ViewComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Display Name", EditorType = EditorType.Text, IsRequired = true, Order = 5,
        HelpText = "Shown in the admin UI.", FormComponent = "Text")]
    public string DisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, Order = 6, FormComponent = "TextArea")]
    public string? Description { get; init; }

    [FormProperty(Label = "Category", EditorType = EditorType.Text, IsRequired = true, Order = 7,
        HelpText = "Grouping category.", FormComponent = "Text")]
    public string Category { get; init; } = "General";

    [FormProperty(Label = "Icon Class", EditorType = EditorType.Text, Order = 8, FormComponent = "Text")]
    public string? IconClass { get; init; }

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 9, FormComponent = "Number")]
    public int Order { get; init; }

    [FormProperty(Label = "Data Type Names JSON", EditorType = EditorType.TextArea, Order = 10,
        HelpText = "JSON array of CLR type names this component supports.", FormComponent = "TextArea")]
    public string DataTypeNamesJson { get; init; } = "[]";

    [FormProperty(Label = "Editor Type Alias", EditorType = EditorType.Text, Order = 11,
        HelpText = "Optional EditorType shorthand this component answers to.", FormComponent = "Text")]
    public string? EditorTypeAlias { get; init; }

    [FormProperty(Label = "Is Default For Type", EditorType = EditorType.Checkbox, Order = 12,
        HelpText = "Whether this is the default component for its DataTypes.", FormComponent = "Checkbox")]
    public bool IsDefaultForType { get; init; }

    [FormProperty(Label = "Write View Name", EditorType = EditorType.Text, Order = 13, FormComponent = "Text")]
    public string WriteViewName { get; init; } = "Write";

    [FormProperty(Label = "Read View Name", EditorType = EditorType.Text, Order = 14, FormComponent = "Text")]
    public string ReadViewName { get; init; } = "Read";

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 15, FormComponent = "Checkbox")]
    public bool IsActive { get; init; } = true;
}
