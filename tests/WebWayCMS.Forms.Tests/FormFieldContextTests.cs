using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormFieldContextTests
{
    [Test]
    public void Name_DelegatesToPropertyName()
    {
        var context = CreateContext(prop => prop.Name = "FieldName");
        Assert.That(context.Name, Is.EqualTo("FieldName"));
    }

    [Test]
    public void Label_DelegatesToPropertyLabel()
    {
        var context = CreateContext(prop => prop.Label = "My Label");
        Assert.That(context.Label, Is.EqualTo("My Label"));
    }

    [Test]
    public void HelpText_DelegatesToPropertyHelpText()
    {
        var context = CreateContext(prop => prop.HelpText = "Help me");
        Assert.That(context.HelpText, Is.EqualTo("Help me"));
    }

    [Test]
    public void Placeholder_DelegatesToPropertyPlaceholder()
    {
        var context = CreateContext(prop => prop.Placeholder = "Enter value");
        Assert.That(context.Placeholder, Is.EqualTo("Enter value"));
    }

    [Test]
    public void IsRequired_DelegatesToPropertyIsRequired()
    {
        var context = CreateContext(prop => prop.IsRequired = true);
        Assert.That(context.IsRequired, Is.True);
    }

    [Test]
    public void Min_DelegatesToPropertyMin()
    {
        var context = CreateContext(prop => prop.Min = 5.5);
        Assert.That(context.Min, Is.EqualTo(5.5));
    }

    [Test]
    public void Max_DelegatesToPropertyMax()
    {
        var context = CreateContext(prop => prop.Max = 100);
        Assert.That(context.Max, Is.EqualTo(100));
    }

    [Test]
    public void MaxLength_DelegatesToPropertyMaxLength()
    {
        var context = CreateContext(prop => prop.MaxLength = 20);
        Assert.That(context.MaxLength, Is.EqualTo(20));
    }

    [Test]
    public void Pattern_DelegatesToPropertyPattern()
    {
        var context = CreateContext(prop => prop.Pattern = "^\\d+$");
        Assert.That(context.Pattern, Is.EqualTo("^\\d+$"));
    }

    [Test]
    public void PatternErrorMessage_DelegatesToPropertyPatternErrorMessage()
    {
        var context = CreateContext(prop => prop.PatternErrorMessage = "Invalid");
        Assert.That(context.PatternErrorMessage, Is.EqualTo("Invalid"));
    }

    [Test]
    public void DropdownOptions_DelegatesToPropertyDropdownOptions()
    {
        var opts = new Dictionary<string, string> { ["a"] = "Alpha" };
        var context = CreateContext(prop => prop.DropdownOptions = opts);
        Assert.That(context.DropdownOptions, Is.SameAs(opts));
    }

    [Test]
    public void EntityType_DelegatesToPropertyEntityType()
    {
        var context = CreateContext(prop => prop.EntityType = "Page");
        Assert.That(context.EntityType, Is.EqualTo("Page"));
    }

    [Test]
    public void ViewComponentName_DelegatesToPropertyViewComponentName()
    {
        var context = CreateContext(prop => prop.ViewComponentName = "MyComponent");
        Assert.That(context.ViewComponentName, Is.EqualTo("MyComponent"));
    }

    [Test]
    public void StringValue_DelegatesToFormValueFormatter()
    {
        var context = CreateContext(prop => { }, value: "test");
        Assert.That(context.StringValue, Is.EqualTo("test"));
    }

    [Test]
    public void StringValue_NullValue_ReturnsEmpty()
    {
        var context = CreateContext(prop => { }, value: null);
        Assert.That(context.StringValue, Is.Empty);
    }

    private static FormFieldContext CreateContext(Action<FormPropertyInfo> configureProp, object? value = null)
    {
        var prop = new FormPropertyInfo { PropertyType = typeof(string) };
        configureProp(prop);
        return new FormFieldContext
        {
            Property = prop,
            Value = value,
            InputName = "Field",
            ElementId = "Field",
        };
    }
}
