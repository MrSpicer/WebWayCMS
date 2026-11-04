namespace WebWayCMS.Forms.Tests;

/// <summary>
/// Properties excluded from the JSON round-trip (System.Text.Json [JsonIgnore]) are runtime-only
/// and must not become form fields.
/// </summary>
public sealed class JsonIgnoredModel
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string RuntimeOnly { get; set; } = "nope";

    public string Visible { get; set; } = "yes";
}
