namespace WebWayCMS.Forms.Tests;

/// <summary>Every InferEditorType CLR-type branch (no FormPropertyAttribute so the type drives it).</summary>
public sealed class InferenceModel
{
    public Guid GuidProp { get; set; }
    public bool BoolProp { get; set; }
    public int IntProp { get; set; }
    public long LongProp { get; set; }
    public short ShortProp { get; set; }
    public decimal DecimalProp { get; set; }
    public double DoubleProp { get; set; }
    public float FloatProp { get; set; }
    public DateTime DateTimeProp { get; set; }
    public DateTimeOffset DateTimeOffsetProp { get; set; }
    public DateOnly DateOnlyProp { get; set; }
    public DayOfWeek EnumProp { get; set; }
    public string StringProp { get; set; } = string.Empty;
    public int? NullableIntProp { get; set; }
}
