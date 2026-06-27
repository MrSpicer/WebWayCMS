using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

namespace WebWayCMS.ContentZones.Tests;

/// <summary>Configuration model exercising every ValidateConfiguration branch.</summary>
public sealed class SampleConfig
{
    [FormProperty(IsRequired = true)]
    public string Name { get; set; } = "valid";

    [FormProperty(IsRequired = true, EditorType = EditorType.Guid)]
    public Guid Ref { get; set; } = Guid.NewGuid();

    [FormProperty(IsRequired = true)]
    public string? RequiredNullable { get; set; } = "set";

    [FormProperty(EditorType = EditorType.Number, Min = 1, Max = 10)]
    public int Count { get; set; } = 5;

    [FormProperty(EditorType = EditorType.Number, Min = 1)]
    public int MinOnly { get; set; } = 5;

    [FormProperty(EditorType = EditorType.Number, Max = 100)]
    public int MaxOnly { get; set; } = 5;

    [FormProperty(Min = 1)]
    public int? OptionalNum { get; set; }

    [FormProperty(Min = 1)]
    public string NotANumber { get; set; } = "abc";

    [FormProperty(MaxLength = 3)]
    public string Code { get; set; } = "ok";

    [FormProperty(Pattern = "^[0-9]+$", PatternErrorMessage = "digits only")]
    public string Digits { get; set; } = "123";

    [FormProperty(Pattern = "^[a-z]+$")]
    public string Letters { get; set; } = "abc";
}

/// <summary>A configuration type with no parameterless constructor (CreateDefaultConfiguration fails).</summary>
public sealed class NoDefaultCtorConfig
{
    [FormProperty]
    public string Value { get; set; }

    public NoDefaultCtorConfig(string value) => Value = value;
}

[ContentZoneComponent("Custom Display", typeof(SampleConfig), Category = "Content", Order = 2, Description = "d", IconClass = "fa")]
public class ConfiguredViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

// Same category as ConfiguredViewComponent but lower Order — exercises in-category ordering.
[ContentZoneComponent(DisplayName = "", Category = "Content", Order = 1)]
public class AlphaViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

// A different category to exercise category sorting and the main-list category comparison.
[ContentZoneComponent(Category = "Layout")]
public class Banner : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

// Component whose configuration type cannot be instantiated by Activator.
[ContentZoneComponent("No Ctor", typeof(NoDefaultCtorConfig), Category = "Content")]
public class BrokenConfigViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

// Not decorated — must be ignored by the scanner.
public class IgnoredViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}