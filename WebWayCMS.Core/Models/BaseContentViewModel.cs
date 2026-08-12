using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models;

/// <summary>
/// Base view model for content types that map from ContentDTO.
/// Contains common properties shared across all content view models.
/// </summary>
public abstract class BaseContentViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public Guid? Id { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public Guid? MasterId { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 0, FormComponent = "Hidden")]
    public int? Version { get; set; }

    [Required]
    [StringLength(500, ErrorMessage = "Title cannot be longer than 500 characters.")]
    [FormProperty(Label = "Title", EditorType = EditorType.Text, IsRequired = true, Order = 1, FormComponent = "Text")]
    public string Title { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "Slug cannot be longer than 500 characters.")]
    [FormProperty(Label = "Slug", EditorType = EditorType.Text, Order = 2, HelpText = "URL-friendly identifier. Auto-generated from title if left blank.", FormComponent = "Text")]
    public string? Slug { get; init; }

    [FormProperty(Label = "Publication Date", EditorType = EditorType.DateTime, Group = "Publishing", Order = 10, FormComponent = "DateTime")]
    public DateTime? PublicationDate { get; set; }

    [FormProperty(Label = "Publication End Date", EditorType = EditorType.DateTime, Group = "Publishing", Order = 11, FormComponent = "DateTime")]
    public DateTime? PublicationEndDate { get; set; }

    [FormProperty(Label = "Published", EditorType = EditorType.Checkbox, Group = "Publishing", Order = 12, FormComponent = "Checkbox")]
    public bool IsPublished { get; set; }

    [FormProperty(Label = "Archived", EditorType = EditorType.Checkbox, Group = "Status", Order = 20, FormComponent = "Checkbox")]
    public bool IsArchived { get; set; }

    [FormProperty(Label = "Hidden", EditorType = EditorType.Checkbox, Group = "Status", Order = 21, FormComponent = "Checkbox")]
    public bool IsHidden { get; set; }

    [FormProperty(Label = "Deleted", EditorType = EditorType.Checkbox, Group = "Status", Order = 22, FormComponent = "Checkbox")]
    public bool IsDeleted { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 99, FormComponent = "Hidden")]
    public DateTime? ModificationDate { get; init; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 99, FormComponent = "Hidden")]
    public DateTime? CreationDate { get; init; }

    //todo: custom fields. List<object> maybe with the field value cast to the type
}