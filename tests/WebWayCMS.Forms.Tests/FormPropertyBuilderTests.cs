using System.ComponentModel.DataAnnotations;

using NUnit.Framework;

using WebWayCMS.Attributes;

using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormPropertyBuilderTests
{
    [Test]
    public void BuildPropertyInfos_SkipsReadOnlyAndWriteOnly()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(PartialAccessModel));

        Assert.That(infos.Select(i => i.Name), Is.EquivalentTo(new[] { "ReadWrite" }));
    }

    [Test]
    public void BuildPropertyInfos_SortsByOrderThenName()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(GroupedModel));

        Assert.That(infos.Select(i => i.Name), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void BuildPropertyInfos_UsesAttributeMetadata()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(AllEditorsModel));
        var area = infos.Single(i => i.Name == "Area");

        Assert.Multiple(() =>
        {
            Assert.That(area.Label, Is.EqualTo("Area"));
            Assert.That(area.EditorType, Is.EqualTo(EditorType.TextArea));
            Assert.That(area.Placeholder, Is.EqualTo("ph"));
            Assert.That(area.MaxLength, Is.EqualTo(50));
        });
    }

    [Test]
    public void BuildPropertyInfos_LabelDefaultsToSpacedPropertyName()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(DataAnnotationModel));
        var quantity = infos.Single(i => i.Name == "Quantity");

        Assert.That(quantity.Label, Is.EqualTo("Quantity"));
    }

    [Test]
    public void BuildPropertyInfos_ResolvesDataAnnotationAttributes()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(DataAnnotationModel));
        var quantity = infos.Single(i => i.Name == "Quantity");
        var code = infos.Single(i => i.Name == "Code");

        Assert.Multiple(() =>
        {
            Assert.That(quantity.IsRequired, Is.True);
            Assert.That(quantity.Min, Is.EqualTo(2));
            Assert.That(quantity.Max, Is.EqualTo(8));
            Assert.That(code.MaxLength, Is.EqualTo(15));
            Assert.That(code.Pattern, Is.EqualTo("^[a-z]+$"));
            Assert.That(code.PatternErrorMessage, Is.EqualTo("lower only"));
        });
    }

    [Test]
    public void BuildPropertyInfos_ParsesDropdownOptions()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(AllEditorsModel));
        var choice = infos.Single(i => i.Name == "Choice");

        Assert.That(choice.DropdownOptions, Is.EqualTo(new Dictionary<string, string> { ["a"] = "Alpha", ["b"] = "Beta" }));
    }

    // --- InferEditorType ---

    [TestCase(typeof(Guid), EditorType.Guid)]
    [TestCase(typeof(bool), EditorType.Checkbox)]
    [TestCase(typeof(int), EditorType.Number)]
    [TestCase(typeof(long), EditorType.Number)]
    [TestCase(typeof(short), EditorType.Number)]
    [TestCase(typeof(decimal), EditorType.Number)]
    [TestCase(typeof(double), EditorType.Number)]
    [TestCase(typeof(float), EditorType.Number)]
    [TestCase(typeof(DateTime), EditorType.DateTime)]
    [TestCase(typeof(DateTimeOffset), EditorType.DateTime)]
    [TestCase(typeof(DateOnly), EditorType.Date)]
    [TestCase(typeof(DayOfWeek), EditorType.Dropdown)]
    [TestCase(typeof(string), EditorType.Text)]
    [TestCase(typeof(int?), EditorType.Number)]
    public void InferEditorType_MapsClrTypeToEditor(Type type, EditorType expected)
    {
        Assert.That(FormPropertyBuilder.InferEditorType(type), Is.EqualTo(expected));
    }

    // --- GetDefaultValue ---

    [Test]
    public void GetDefaultValue_ValueType_ReturnsZeroValue()
    {
        Assert.That(FormPropertyBuilder.GetDefaultValue(typeof(int)), Is.EqualTo(0));
    }

    [Test]
    public void GetDefaultValue_ReferenceType_ReturnsNull()
    {
        Assert.That(FormPropertyBuilder.GetDefaultValue(typeof(string)), Is.Null);
    }

    // --- GetMinValue / GetMaxValue ---

    [Test]
    public void GetMinValue_FromAttribute()
    {
        Assert.That(FormPropertyBuilder.GetMinValue(new FormPropertyAttribute { Min = 3 }, null), Is.EqualTo(3));
    }

    [Test]
    public void GetMinValue_FromRangeAttribute_WhenAttrMinIsNaN()
    {
        Assert.That(FormPropertyBuilder.GetMinValue(new FormPropertyAttribute(), new RangeAttribute(1, 9)), Is.EqualTo(1));
    }

    [Test]
    public void GetMinValue_FromRangeAttribute_WhenAttrNull()
    {
        Assert.That(FormPropertyBuilder.GetMinValue(null, new RangeAttribute(4, 9)), Is.EqualTo(4));
    }

    [Test]
    public void GetMinValue_RangeMinimumNotParseable_ReturnsNull()
    {
        var range = new RangeAttribute(typeof(DateTime), "2020-01-01", "2020-12-31");

        Assert.That(FormPropertyBuilder.GetMinValue(null, range), Is.Null);
    }

    [Test]
    public void GetMinValue_NoSources_ReturnsNull()
    {
        Assert.That(FormPropertyBuilder.GetMinValue(null, null), Is.Null);
    }

    [Test]
    public void GetMaxValue_FromAttribute()
    {
        Assert.That(FormPropertyBuilder.GetMaxValue(new FormPropertyAttribute { Max = 7 }, null), Is.EqualTo(7));
    }

    [Test]
    public void GetMaxValue_FromRangeAttribute()
    {
        Assert.That(FormPropertyBuilder.GetMaxValue(null, new RangeAttribute(1, 9)), Is.EqualTo(9));
    }

    [Test]
    public void GetMaxValue_RangeMaximumNotParseable_ReturnsNull()
    {
        var range = new RangeAttribute(typeof(DateTime), "2020-01-01", "2020-12-31");

        Assert.That(FormPropertyBuilder.GetMaxValue(null, range), Is.Null);
    }

    [Test]
    public void GetMaxValue_NoSources_ReturnsNull()
    {
        Assert.That(FormPropertyBuilder.GetMaxValue(null, null), Is.Null);
    }

    // --- GetMaxLengthValue ---

    [Test]
    public void GetMaxLengthValue_FromAttribute()
    {
        Assert.That(FormPropertyBuilder.GetMaxLengthValue(new FormPropertyAttribute { MaxLength = 12 }, null), Is.EqualTo(12));
    }

    [Test]
    public void GetMaxLengthValue_FromStringLength_WhenAttrUnset()
    {
        Assert.That(FormPropertyBuilder.GetMaxLengthValue(new FormPropertyAttribute(), new StringLengthAttribute(20)), Is.EqualTo(20));
    }

    [Test]
    public void GetMaxLengthValue_FromStringLength_WhenAttrNull()
    {
        Assert.That(FormPropertyBuilder.GetMaxLengthValue(null, new StringLengthAttribute(25)), Is.EqualTo(25));
    }

    [Test]
    public void GetMaxLengthValue_NoSources_ReturnsNull()
    {
        Assert.That(FormPropertyBuilder.GetMaxLengthValue(new FormPropertyAttribute(), null), Is.Null);
    }

    // --- ParseDropdownOptions ---

    [Test]
    public void ParseDropdownOptions_ValueLabelPairsAndBareValues()
    {
        var result = FormPropertyBuilder.ParseDropdownOptions("a:Alpha, b ,c:Gamma");

        Assert.That(result, Is.EqualTo(new Dictionary<string, string>
        {
            ["a"] = "Alpha",
            ["b"] = "b",
            ["c"] = "Gamma"
        }));
    }

    // --- InsertSpaces ---

    [Test]
    public void InsertSpaces_InsertsBeforeInteriorCapitals()
    {
        Assert.That(FormPropertyBuilder.InsertSpaces("MyPropertyName"), Is.EqualTo("My Property Name"));
    }

    [TestCase("")]
    [TestCase(null)]
    public void InsertSpaces_NullOrEmpty_ReturnsInput(string? input)
    {
        Assert.That(FormPropertyBuilder.InsertSpaces(input!), Is.EqualTo(input));
    }
}