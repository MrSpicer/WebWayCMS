namespace WebWayCMS.Forms.Tests;

/// <summary>A configuration type <see cref="FormPropertyBuilder"/> cannot describe (its attribute throws).</summary>
public sealed class UndescribableModel
{
    [ThrowingRequired]
    public string? Name { get; set; }
}
