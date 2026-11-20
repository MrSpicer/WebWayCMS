using System.ComponentModel.DataAnnotations;

namespace WebWayCMS.Forms.Tests;

/// <summary>A [Required] whose construction fails, so describing a model that uses it throws.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ThrowingRequiredAttribute : RequiredAttribute
{
    public ThrowingRequiredAttribute() => throw new InvalidOperationException("broken attribute");
}
