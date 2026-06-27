using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models;

/// <summary>
/// Base view model for content types that map from ContentDTO.
/// Contains common properties shared across all content view models.
/// </summary>
public abstract class BaseContentViewModel
{
    [FormProperty(EditorType = EditorType.Hidden, Order = 0)]
    public Guid? Id { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 0)]
    public Guid? MasterId { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 0)]
    public int? Version { get; set; }

    [Required]
    [StringLength(500, ErrorMessage = "Title cannot be longer than 500 characters.")]
    [FormProperty(Label = "Title", EditorType = EditorType.Text, IsRequired = true, Order = 1)]
    public string Title { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "Slug cannot be longer than 500 characters.")]
    [FormProperty(Label = "Slug", EditorType = EditorType.Text, Order = 2, HelpText = "URL-friendly identifier. Auto-generated from title if left blank.")]
    public string? Slug { get; init; }

    [FormProperty(Label = "Publication Date", EditorType = EditorType.DateTime, Group = "Publishing", Order = 10)]
    public DateTime? PublicationDate { get; set; }

    [FormProperty(Label = "Publication End Date", EditorType = EditorType.DateTime, Group = "Publishing", Order = 11)]
    public DateTime? PublicationEndDate { get; set; }

    [FormProperty(Label = "Published", EditorType = EditorType.Checkbox, Group = "Publishing", Order = 12)]
    public bool IsPublished { get; set; }

    [FormProperty(Label = "Archived", EditorType = EditorType.Checkbox, Group = "Status", Order = 20)]
    public bool IsArchived { get; set; }

    [FormProperty(Label = "Hidden", EditorType = EditorType.Checkbox, Group = "Status", Order = 21)]
    public bool IsHidden { get; set; }

    [FormProperty(Label = "Deleted", EditorType = EditorType.Checkbox, Group = "Status", Order = 22)]
    public bool IsDeleted { get; set; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 99)]
    public DateTime? ModificationDate { get; init; }

    [FormProperty(EditorType = EditorType.Hidden, Order = 99)]
    public DateTime? CreationDate { get; init; }

    //todo: custom fields. List<object> maybe with the field value cast to the type
}