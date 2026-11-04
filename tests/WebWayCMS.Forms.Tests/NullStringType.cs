namespace WebWayCMS.Forms.Tests;

/// <summary>A type whose ToString() returns null — used to exercise null-coalescing fallbacks.</summary>
public sealed class NullStringType
{
    public override string? ToString() => null;
}
