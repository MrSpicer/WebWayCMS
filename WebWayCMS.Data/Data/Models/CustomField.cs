namespace WebWayCMS.Data.Models;

public record CustomField
{
    public string FieldName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}