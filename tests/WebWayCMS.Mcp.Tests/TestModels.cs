using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Mcp.Tests;

/// <summary>
/// A stand-in upsert view model exercising every <see cref="McpToolHelpers.DescribeFields"/>
/// and <see cref="McpToolHelpers.Merge"/> branch: required via FormProperty, required via
/// [Required], an optional field with help text, a hidden field, and a field with no FormProperty.
/// </summary>
public sealed class FakeUpsertViewModel
{
    [FormProperty(Label = "Title", IsRequired = true, Order = 2)]
    public string Title { get; init; } = string.Empty;

    [FormProperty(Label = "Body", EditorType = EditorType.RichText, Order = 3, HelpText = "The body")]
    public string Body { get; set; } = string.Empty;

    // Label intentionally empty -> falls back to the property name; required via [Required].
    [Required]
    [FormProperty(Order = 4)]
    public string Slug { get; set; } = string.Empty;

    [FormProperty(EditorType = EditorType.Hidden, Order = 1)]
    public Guid? Id { get; set; }

    // No FormProperty -> excluded from describe output.
    public int Ignored { get; set; }
}