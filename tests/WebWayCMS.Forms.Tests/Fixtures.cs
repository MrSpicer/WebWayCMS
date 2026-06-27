using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

/// <summary>A type whose ToString() returns null — used to exercise null-coalescing fallbacks.</summary>
public sealed class NullStringType
{
    public override string? ToString() => null;
}

/// <summary>Covers every EditorType branch plus value-formatting paths in the tag helper.</summary>
public sealed class AllEditorsModel
{
    [FormProperty("Plain Text", EditorType.Text)]
    public string Text { get; set; } = "hello";

    [FormProperty(Label = "Area", EditorType = EditorType.TextArea, Placeholder = "ph", HelpText = "help", MaxLength = 50)]
    public string Area { get; set; } = "multi";

    [FormProperty(EditorType = EditorType.RichText, IsRequired = true)]
    public string Rich { get; set; } = "<b>x</b>";

    [FormProperty(EditorType = EditorType.Number, Min = 1, Max = 10)]
    public int Count { get; set; } = 5;

    [FormProperty(EditorType = EditorType.Checkbox, HelpText = "check help", CssClass = "extra", IsRequired = true)]
    public bool Flag { get; set; } = true;

    [FormProperty(EditorType = EditorType.Guid)]
    public Guid Identifier { get; set; } = Guid.NewGuid();

    [FormProperty(EditorType = EditorType.Dropdown, DropdownOptions = "a:Alpha,b:Beta")]
    public string Choice { get; set; } = "a";

    [FormProperty(EditorType = EditorType.Date)]
    public DateTime Day { get; set; } = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTime Moment { get; set; } = new(2024, 1, 2, 3, 4, 0, DateTimeKind.Utc);

    [FormProperty(EditorType = EditorType.Color)]
    public string Colour { get; set; } = "#fff";

    [FormProperty(EditorType = EditorType.Url, Placeholder = "https://", HelpText = "u", IsRequired = true, Pattern = "^https?://")]
    public string Link { get; set; } = "https://x";

    [FormProperty(EditorType = EditorType.Email)]
    public string Mail { get; set; } = "a@b.c";

    [FormProperty(EditorType = EditorType.ViewPicker, ViewComponentName = "Article")]
    public string View { get; set; } = "Default";

    [FormProperty(EditorType = EditorType.PageControllerPicker)]
    public string Controller { get; set; } = "GenericPage";

    [FormProperty(EditorType = EditorType.Hidden)]
    public string Secret { get; set; } = "s";
}

/// <summary>Read-only and write-only properties are skipped by the builder.</summary>
public sealed class PartialAccessModel
{
    public string ReadWrite { get; set; } = "ok";
    public string ReadOnly { get; } = "ro";
    private string _writeOnly = string.Empty;
    public string WriteOnly { set => _writeOnly = value; }
}

/// <summary>Empty model produces no form fields.</summary>
public sealed class EmptyModel
{
}

/// <summary>Two grouped sections plus an ungrouped trailing field.</summary>
public sealed class GroupedModel
{
    [FormProperty(Group = "First", Order = 1)]
    public string A { get; set; } = "a";

    [FormProperty(Group = "Second", Order = 2)]
    public string B { get; set; } = "b";

    [FormProperty(Order = 3)]
    public string C { get; set; } = "c";
}

/// <summary>The final property keeps GroupWithNext set, exercising the loop's no-break exit.</summary>
public sealed class HorizontalGroupModel
{
    [FormProperty(GroupWithNext = true, Order = 1)]
    public string Left { get; set; } = "l";

    [FormProperty(GroupWithNext = true, Order = 2)]
    public string Right { get; set; } = "r";
}

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

/// <summary>Default/empty values to exercise the "empty string" formatting paths.</summary>
public sealed class DefaultValuesModel
{
    [FormProperty(EditorType = EditorType.Guid)]
    public Guid EmptyGuid { get; set; } = Guid.Empty;

    [FormProperty(EditorType = EditorType.Date)]
    public DateTime MinDate { get; set; } = DateTime.MinValue;

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTime MinMoment { get; set; } = DateTime.MinValue;

    [FormProperty(EditorType = EditorType.Date)]
    public DateOnly DateOnlyValue { get; set; } = DateOnly.MinValue;

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTimeOffset MinOffset { get; set; } = DateTimeOffset.MinValue;

    [FormProperty(EditorType = EditorType.Text)]
    public NullStringType? Weird { get; set; } = new NullStringType();
}

/// <summary>Checkbox permutations: unchecked, no CSS, no help text, not required.</summary>
public sealed class PlainCheckboxModel
{
    [FormProperty(EditorType = EditorType.Checkbox)]
    public bool Flag { get; set; } = false;
}

/// <summary>A standard field carrying a CSS class, and a required select with help text.</summary>
public sealed class StyledFieldsModel
{
    [FormProperty(EditorType = EditorType.Text, CssClass = "my-css", Order = 1)]
    public string Styled { get; set; } = "v";

    [FormProperty(EditorType = EditorType.Dropdown, DropdownOptions = "a:A,b:B", IsRequired = true, HelpText = "pick", Order = 2)]
    public string Pick { get; set; } = "a";

    [FormProperty(EditorType = EditorType.TextArea, IsRequired = true, Order = 3)]
    public string RequiredArea { get; set; } = "text";
}

/// <summary>A horizontal group that ends with a non-grouped field (exercises the break path).</summary>
public sealed class HorizontalGroupBreakModel
{
    [FormProperty(GroupWithNext = true, Order = 1)]
    public string Left { get; set; } = "l";

    [FormProperty(GroupWithNext = false, Order = 2)]
    public string Right { get; set; } = "r";
}

/// <summary>Null values: a checkbox bound to a null bool? (not a bool) and a null text field.</summary>
public sealed class NullValueModel
{
    [FormProperty(EditorType = EditorType.Checkbox, Order = 1)]
    public bool? MaybeFlag { get; set; }

    [FormProperty(EditorType = EditorType.Text, Order = 2)]
    public string? Missing { get; set; }
}

/// <summary>The final property belongs to a group, so a group div stays open at the end.</summary>
public sealed class EndsInGroupModel
{
    [FormProperty(Group = "Only", Order = 1)]
    public string A { get; set; } = "a";

    [FormProperty(Group = "Only", Order = 2)]
    public string B { get; set; } = "b";
}

/// <summary>Non-minimum DateOnly / DateTimeOffset values for the formatted paths.</summary>
public sealed class TemporalModel
{
    [FormProperty(EditorType = EditorType.Date)]
    public DateOnly Day { get; set; } = new(2024, 5, 6);

    [FormProperty(EditorType = EditorType.DateTime)]
    public DateTimeOffset Moment { get; set; } = new(2024, 5, 6, 7, 8, 0, TimeSpan.Zero);
}

/// <summary>A select whose stored value does not match any option, and a non-empty ViewPicker value.</summary>
public sealed class SelectModel
{
    [FormProperty(EditorType = EditorType.Dropdown, DropdownOptions = "x:X,y:Y")]
    public string Unmatched { get; set; } = "z";

    [FormProperty(EditorType = EditorType.ViewPicker, ViewComponentName = "Page")]
    public string Picked { get; set; } = "Custom";

    [FormProperty(EditorType = EditorType.PageControllerPicker)]
    public string EmptyController { get; set; } = string.Empty;
}

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