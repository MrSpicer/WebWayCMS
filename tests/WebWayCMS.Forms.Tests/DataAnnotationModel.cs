using System.ComponentModel.DataAnnotations;

namespace WebWayCMS.Forms.Tests;

/// <summary>Validation attributes resolved without a FormPropertyAttribute present.</summary>
public sealed class DataAnnotationModel
{
    [Required]
    [Range(2, 8)]
    public int Quantity { get; set; }

    [StringLength(15)]
    [RegularExpression("^[a-z]+$", ErrorMessage = "lower only")]
    public string Code { get; set; } = string.Empty;
}
