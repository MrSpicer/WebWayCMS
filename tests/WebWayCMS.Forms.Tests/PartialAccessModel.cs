namespace WebWayCMS.Forms.Tests;

/// <summary>Read-only and write-only properties are skipped by the builder.</summary>
public sealed class PartialAccessModel
{
    public string ReadWrite { get; set; } = "ok";
    public string ReadOnly { get; } = "ro";
    private string _writeOnly = string.Empty;
    public string WriteOnly { set => _writeOnly = value; }
}
