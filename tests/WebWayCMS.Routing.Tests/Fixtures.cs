using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

namespace WebWayCMS.Routing.Tests;

public sealed class SamplePageConfig
{
    [FormProperty(IsRequired = true)]
    public string Title { get; set; } = "valid";

    [FormProperty(EditorType = EditorType.Number, Min = 1, Max = 10)]
    public int PageSize { get; set; } = 5;

    [FormProperty(EditorType = EditorType.Number, Min = 1)]
    public int MinOnly { get; set; } = 5;

    [FormProperty(EditorType = EditorType.Number, Max = 100)]
    public int MaxOnly { get; set; } = 5;

    [FormProperty(Min = 1)]
    public int? OptionalNum { get; set; }

    [FormProperty(IsRequired = true, EditorType = EditorType.Guid)]
    public Guid Ref { get; set; } = Guid.NewGuid();

    [FormProperty(Min = 1)]
    public string NotANumber { get; set; } = "abc";

    [FormProperty(MaxLength = 3)]
    public string Code { get; set; } = "ok";

    [FormProperty(Pattern = "^[0-9]+$", PatternErrorMessage = "digits only")]
    public string Digits { get; set; } = "123";

    [FormProperty(Pattern = "^[a-z]+$")]
    public string Letters { get; set; } = "abc";
}

public sealed class NoDefaultCtorConfig
{
    [FormProperty]
    public string Value { get; set; }

    public NoDefaultCtorConfig(string value) => Value = value;
}

[PageController("Custom Display", typeof(SamplePageConfig), Category = "Content", Order = 2, Description = "d", IconClass = "fa")]
public class ConfiguredController : Controller
{
}

[PageController(DisplayName = "", Category = "Content", Order = 1)]
public class AlphaController : Controller
{
}

[PageController(Category = "Layout")]
public class Banner : Controller
{
}

[PageController("No Ctor", typeof(NoDefaultCtorConfig), Category = "Content")]
public class BrokenConfigController : Controller
{
}

public class IgnoredController : Controller
{
}