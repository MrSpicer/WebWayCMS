using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentTypeRegistration;

/// <summary>
/// The edit form of a content type registration. <see cref="ContentTypeKey"/> and <see cref="ClrTypeName"/> are
/// code-owned: shown above the form (hidden fields, left out of <c>describe_content_type</c>) and ignored on save.
/// </summary>
public sealed class ContentTypeRegistrationUpsertViewModel : BaseContentViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public string ContentTypeKey { get; init; } = string.Empty;

    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public string ClrTypeName { get; init; } = string.Empty;

    [FormProperty(Label = "Display Name", EditorType = EditorType.Text, IsRequired = true, MaxLength = 256, Order = 3,
        HelpText = "The singular name, e.g. 'Article List' (\"New Article List\" buttons, entity pickers).", FormComponent = "Text")]
    public string DisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Plural Display Name", EditorType = EditorType.Text, IsRequired = true, MaxLength = 256, Order = 4,
        HelpText = "The plural name, e.g. 'Article Lists' (admin navbar, children panels).", FormComponent = "Text")]
    public string PluralDisplayName { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, MaxLength = 2000, Order = 5, FormComponent = "TextArea")]
    public string? Description { get; init; }

    [FormProperty(Label = "Category", EditorType = EditorType.Text, IsRequired = true, MaxLength = 128, Order = 6,
        HelpText = "Grouping category (e.g. 'Content', 'System').", FormComponent = "Text")]
    public string Category { get; init; } = "General";

    [FormProperty(Label = "Icon Class", EditorType = EditorType.Text, MaxLength = 128, Order = 7,
        HelpText = "CSS icon class (e.g. 'fas fa-file').", FormComponent = "Text")]
    public string? IconClass { get; init; }

    [FormProperty(Label = "Order", EditorType = EditorType.Number, Order = 8,
        HelpText = "Sort order within the category.", FormComponent = "Number")]
    public int Order { get; init; }
}
