using System.Text.Encodings.Web;

using NUnit.Framework;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormAttributeBuilderTests
{
    private static FormFieldContext CreateField(
        string name = "TestProp",
        string inputName = "TestProp",
        string elementId = "TestProp",
        bool jsonBound = false,
        object? value = null,
        string helpText = "",
        string placeholder = "",
        int? maxLength = null,
        string pattern = "",
        string patternErrorMessage = "",
        double? min = null,
        double? max = null,
        bool isRequired = false,
        string cssClass = "")
    {
        var prop = new FormPropertyInfo
        {
            Name = name,
            Label = name,
            HelpText = helpText,
            Placeholder = placeholder,
            MaxLength = maxLength,
            Pattern = pattern,
            PatternErrorMessage = patternErrorMessage,
            Min = min,
            Max = max,
            IsRequired = isRequired,
            CssClass = cssClass,
            PropertyType = typeof(string),
        };
        return new FormFieldContext
        {
            Property = prop,
            Value = value,
            InputName = inputName,
            ElementId = elementId,
            JsonBound = jsonBound,
        };
    }

    // ── For ────────────────────────────────────────────────────────────

    [Test]
    public void For_NullField_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => FormAttributeBuilder.For(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("field"));
    }

    [Test]
    public void For_ReturnsNewInstance()
    {
        var field = CreateField();

        var builder = FormAttributeBuilder.For(field);

        Assert.That(builder, Is.Not.Null);
    }

    // ── Attr ───────────────────────────────────────────────────────────

    [Test]
    public void Attr_AppendsNameAndEncodedValue()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Attr("type", "text").Build();

        Assert.That(result, Is.EqualTo(" type=\"text\""));
    }

    [Test]
    public void Attr_EncodesSpecialCharactersInValue()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Attr("data-x", "a&b<c>d\"e").Build();

        Assert.That(result, Is.EqualTo($" data-x=\"{HtmlEncoder.Default.Encode("a&b<c>d\"e")}\""));
    }

    [Test]
    public void Attr_AppendsMultipleAttributes()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field)
            .Attr("type", "text")
            .Attr("name", "field1")
            .Build();

        Assert.That(result, Is.EqualTo(" type=\"text\" name=\"field1\""));
    }

    // ── Raw ────────────────────────────────────────────────────────────

    [Test]
    public void Raw_AppendsLiteralString()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Raw("required").Build();

        Assert.That(result, Is.EqualTo(" required"));
    }

    [Test]
    public void Raw_DoesNotEncodeValue()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Raw("data-x=\"a&b\"").Build();

        Assert.That(result, Is.EqualTo(" data-x=\"a&b\""));
    }

    // ── Type ───────────────────────────────────────────────────────────

    [Test]
    public void Type_DelegatesToAttr()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Type("number").Build();

        Assert.That(result, Is.EqualTo(" type=\"number\""));
    }

    // ── Css ────────────────────────────────────────────────────────────

    [Test]
    public void Css_AppendsBaseClass()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Css("input").Build();

        Assert.That(result, Is.EqualTo(" class=\"input\""));
    }

    [Test]
    public void Css_AppendsPropertyCssClass()
    {
        var field = CreateField(cssClass: "is-primary");
        var result = FormAttributeBuilder.For(field).Css("input").Build();

        Assert.That(result, Is.EqualTo(" class=\"input is-primary\""));
    }

    [Test]
    public void Css_NoPropertyCssClass_UsesOnlyBase()
    {
        var field = CreateField(cssClass: "");
        var result = FormAttributeBuilder.For(field).Css("select").Build();

        Assert.That(result, Is.EqualTo(" class=\"select\""));
    }

    // ── Naming ─────────────────────────────────────────────────────────

    [Test]
    public void Naming_WithInputName_EmitsNameAndId()
    {
        var field = CreateField(name: "Title", inputName: "Title", elementId: "TitleField");
        var result = FormAttributeBuilder.For(field).Naming().Build();

        Assert.That(result, Does.Contain(" name=\"Title\""));
        Assert.That(result, Does.Contain(" id=\"TitleField\""));
    }

    [Test]
    public void Naming_WithJsonBound_EmitsDataProp()
    {
        var field = CreateField(name: "Title", inputName: "", jsonBound: true, elementId: "TitleField");
        var result = FormAttributeBuilder.For(field).Naming().Build();

        Assert.That(result, Does.Contain(" data-prop=\"Title\""));
    }

    [Test]
    public void Naming_WithBoth_EmitsNameIdAndDataProp()
    {
        var field = CreateField(name: "Title", inputName: "Title", elementId: "TitleField", jsonBound: true);
        var result = FormAttributeBuilder.For(field).Naming().Build();

        Assert.That(result, Does.Contain(" name=\"Title\""));
        Assert.That(result, Does.Contain(" id=\"TitleField\""));
        Assert.That(result, Does.Contain(" data-prop=\"Title\""));
    }

    [Test]
    public void Naming_WithNeitherInputNameNorJsonBound_EmitsOnlyId()
    {
        var field = CreateField(inputName: "", jsonBound: false, elementId: "MyId");
        var result = FormAttributeBuilder.For(field).Naming().Build();

        Assert.That(result, Does.Contain(" id=\"MyId\""));
        Assert.That(result, Does.Not.Contain("name"));
        Assert.That(result, Does.Not.Contain("data-prop"));
    }

    // ── Value ──────────────────────────────────────────────────────────

    [Test]
    public void Value_UsesStringValue()
    {
        var field = CreateField(value: "hello");
        var result = FormAttributeBuilder.For(field).Value().Build();

        Assert.That(result, Is.EqualTo(" value=\"hello\""));
    }

    [Test]
    public void Value_ExplicitString_UsesExplicitValue()
    {
        var field = CreateField(value: "hello");
        var result = FormAttributeBuilder.For(field).Value("world").Build();

        Assert.That(result, Is.EqualTo(" value=\"world\""));
    }

    // ── Placeholder ────────────────────────────────────────────────────

    [Test]
    public void Placeholder_WhenNonEmpty_EmitsPlaceholder()
    {
        var field = CreateField(placeholder: "Enter text");
        var result = FormAttributeBuilder.For(field).Placeholder().Build();

        Assert.That(result, Is.EqualTo(" placeholder=\"Enter text\""));
    }

    [Test]
    public void Placeholder_WhenEmpty_SkipsAttribute()
    {
        var field = CreateField(placeholder: "");
        var result = FormAttributeBuilder.For(field).Placeholder().Build();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Placeholder_WhenNull_SkipsAttribute()
    {
        var field = CreateField(placeholder: null!);
        var result = FormAttributeBuilder.For(field).Placeholder().Build();

        Assert.That(result, Is.Empty);
    }

    // ── MaxLength ──────────────────────────────────────────────────────

    [Test]
    public void MaxLength_WhenSet_EmitsMaxLength()
    {
        var field = CreateField(maxLength: 50);
        var result = FormAttributeBuilder.For(field).MaxLength().Build();

        Assert.That(result, Is.EqualTo(" maxlength=\"50\""));
    }

    [Test]
    public void MaxLength_WhenNull_SkipsAttribute()
    {
        var field = CreateField(maxLength: null);
        var result = FormAttributeBuilder.For(field).MaxLength().Build();

        Assert.That(result, Is.Empty);
    }

    // ── Pattern ────────────────────────────────────────────────────────

    [Test]
    public void Pattern_WhenSet_EmitsPattern()
    {
        var field = CreateField(pattern: "^[a-z]+$");
        var result = FormAttributeBuilder.For(field).Pattern().Build();

        Assert.That(result, Does.Contain(" pattern=\"" + HtmlEncoder.Default.Encode("^[a-z]+$") + "\""));
    }

    [Test]
    public void Pattern_WithErrorMessage_EmitsPatternAndTitle()
    {
        var field = CreateField(pattern: "^[a-z]+$", patternErrorMessage: "lowercase only");
        var result = FormAttributeBuilder.For(field).Pattern().Build();

        Assert.That(result, Does.Contain(" pattern=\"" + HtmlEncoder.Default.Encode("^[a-z]+$") + "\""));
        Assert.That(result, Does.Contain(" title=\"lowercase only\""));
    }

    [Test]
    public void Pattern_WhenEmpty_SkipsAttribute()
    {
        var field = CreateField(pattern: "");
        var result = FormAttributeBuilder.For(field).Pattern().Build();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Pattern_WhenSetButNoErrorMessage_EmitsOnlyPattern()
    {
        var field = CreateField(pattern: "\\d+", patternErrorMessage: "");
        var result = FormAttributeBuilder.For(field).Pattern().Build();

        Assert.That(result, Does.Contain(" pattern=\"" + HtmlEncoder.Default.Encode("\\d+") + "\""));
        Assert.That(result, Does.Not.Contain("title"));
    }

    // ── MinMax ─────────────────────────────────────────────────────────

    [Test]
    public void MinMax_WhenBothSet_EmitsBoth()
    {
        var field = CreateField(min: 1, max: 10);
        var result = FormAttributeBuilder.For(field).MinMax().Build();

        Assert.That(result, Does.Contain(" min=\"1\""));
        Assert.That(result, Does.Contain(" max=\"10\""));
    }

    [Test]
    public void MinMax_WhenOnlyMin_EmitsOnlyMin()
    {
        var field = CreateField(min: 5, max: null);
        var result = FormAttributeBuilder.For(field).MinMax().Build();

        Assert.That(result, Is.EqualTo(" min=\"5\""));
    }

    [Test]
    public void MinMax_WhenOnlyMax_EmitsOnlyMax()
    {
        var field = CreateField(min: null, max: 20);
        var result = FormAttributeBuilder.For(field).MinMax().Build();

        Assert.That(result, Is.EqualTo(" max=\"20\""));
    }

    [Test]
    public void MinMax_WhenBothNull_SkipsAttribute()
    {
        var field = CreateField(min: null, max: null);
        var result = FormAttributeBuilder.For(field).MinMax().Build();

        Assert.That(result, Is.Empty);
    }

    // ── Required ───────────────────────────────────────────────────────

    [Test]
    public void Required_WhenTrue_EmitsRequiredAndAria()
    {
        var field = CreateField(isRequired: true);
        var result = FormAttributeBuilder.For(field).Required().Build();

        Assert.That(result, Is.EqualTo(" required aria-required=\"true\""));
    }

    [Test]
    public void Required_WhenFalse_SkipsAttribute()
    {
        var field = CreateField(isRequired: false);
        var result = FormAttributeBuilder.For(field).Required().Build();

        Assert.That(result, Is.Empty);
    }

    // ── DescribedBy ────────────────────────────────────────────────────

    [Test]
    public void DescribedBy_WhenHelpText_EmitsAriaDescribedby()
    {
        var field = CreateField(helpText: "Enter your name", elementId: "Name");
        var result = FormAttributeBuilder.For(field).DescribedBy().Build();

        Assert.That(result, Is.EqualTo(" aria-describedby=\"Name_help\""));
    }

    [Test]
    public void DescribedBy_EncodesElementIdInHelpId()
    {
        var field = CreateField(helpText: "Help", elementId: "My&Field");
        var result = FormAttributeBuilder.For(field).DescribedBy().Build();

        Assert.That(result, Is.EqualTo($" aria-describedby=\"{HtmlEncoder.Default.Encode("My&Field")}_help\""));
    }

    [Test]
    public void DescribedBy_WhenNoHelpText_SkipsAttribute()
    {
        var field = CreateField(helpText: "");
        var result = FormAttributeBuilder.For(field).DescribedBy().Build();

        Assert.That(result, Is.Empty);
    }

    // ── Data ───────────────────────────────────────────────────────────

    [Test]
    public void Data_WhenNonEmpty_EmitsDataAttribute()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Data("source", "db").Build();

        Assert.That(result, Is.EqualTo(" data-source=\"db\""));
    }

    [Test]
    public void Data_WhenEmpty_SkipsAttribute()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Data("source", "").Build();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Data_EncodesValue()
    {
        var field = CreateField();
        var result = FormAttributeBuilder.For(field).Data("x", "a&b").Build();

        Assert.That(result, Is.EqualTo($" data-x=\"{HtmlEncoder.Default.Encode("a&b")}\""));
    }

    // ── Build ──────────────────────────────────────────────────────────

    [Test]
    public void Build_ReturnsFullAttributeString()
    {
        var field = CreateField(inputName: "Email");
        var result = FormAttributeBuilder.For(field)
            .Type("email")
            .Naming()
            .Placeholder()
            .Build();

        Assert.That(result, Is.EqualTo(" type=\"email\" name=\"Email\" id=\"TestProp\""));
    }

    // ── Chaining ───────────────────────────────────────────────────────

    [Test]
    public void Chaining_AllMethodsReturnBuilder()
    {
        var field = CreateField(maxLength: 50, min: 1, max: 10, isRequired: true,
            placeholder: "ph", pattern: "\\d+", helpText: "help", elementId: "FieldId");

        Assert.That(FormAttributeBuilder.For(field).Attr("x", "y"), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Raw("x"), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Type("text"), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Css("c"), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Naming(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Value(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Value("v"), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Placeholder(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).MaxLength(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Pattern(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).MinMax(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Required(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).DescribedBy(), Is.TypeOf<FormAttributeBuilder>());
        Assert.That(FormAttributeBuilder.For(field).Data("d", "v"), Is.TypeOf<FormAttributeBuilder>());
    }

    // ── Integration ────────────────────────────────────────────────────

    [Test]
    public void Integration_ComplexField_ProducesFullAttributeString()
    {
        var field = CreateField(
            name: "Email",
            inputName: "Email",
            elementId: "Email",
            value: "a@b.c",
            placeholder: "Enter email",
            maxLength: 100,
            pattern: "^.+@.+$",
            patternErrorMessage: "Invalid email",
            min: 1,
            max: 10,
            isRequired: true,
            cssClass: "is-medium",
            helpText: "Your work email");

        var result = FormAttributeBuilder.For(field)
            .Type("email")
            .Css("input")
            .Naming()
            .Value()
            .Placeholder()
            .MaxLength()
            .Pattern()
            .MinMax()
            .Required()
            .DescribedBy()
            .Data("source", "form")
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(" type=\"email\""));
            Assert.That(result, Does.Contain(" class=\"input is-medium\""));
            Assert.That(result, Does.Contain(" name=\"Email\""));
            Assert.That(result, Does.Contain(" id=\"Email\""));
            Assert.That(result, Does.Contain(" value=\"a@b.c\""));
            Assert.That(result, Does.Contain(" placeholder=\"Enter email\""));
            Assert.That(result, Does.Contain(" maxlength=\"100\""));
            Assert.That(result, Does.Contain(" pattern=\"" + HtmlEncoder.Default.Encode("^.+@.+$") + "\""));
            Assert.That(result, Does.Contain(" title=\"Invalid email\""));
            Assert.That(result, Does.Contain(" min=\"1\""));
            Assert.That(result, Does.Contain(" max=\"10\""));
            Assert.That(result, Does.Contain(" required aria-required=\"true\""));
            Assert.That(result, Does.Contain(" aria-describedby=\"Email_help\""));
            Assert.That(result, Does.Contain(" data-source=\"form\""));
        });
    }
}
