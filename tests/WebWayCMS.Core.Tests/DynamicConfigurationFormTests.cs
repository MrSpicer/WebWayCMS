using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using NUnit.Framework;

using WebWayCMS.Forms;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class DynamicConfigurationFormTests
{
    public class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class NoDefaultCtorConfig
    {
        public NoDefaultCtorConfig(string name)
        {
            Name = name;
        }
        public string Name { get; set; } = string.Empty;
    }

    [Test]
    public void Materialize_NullValuesJson_CreatesFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(typeof(TestConfig), null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
        Assert.That(((TestConfig)result!).Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Materialize_EmptyStringValuesJson_CreatesFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(typeof(TestConfig), string.Empty);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
        Assert.That(((TestConfig)result!).Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Materialize_WhitespaceValuesJson_CreatesFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(typeof(TestConfig), "   ");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
    }

    [Test]
    public void Materialize_EmptyObjectJson_CreatesFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(typeof(TestConfig), "{}");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
    }

    [Test]
    public void Materialize_WhitespacePaddedEmptyObjectJson_CreatesFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(typeof(TestConfig), "  {}  ");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
    }

    [Test]
    public void Materialize_ValidJson_DeserializesCorrectly()
    {
        var result = DynamicConfigurationForm.Materialize(
            typeof(TestConfig),
            "{\"Name\":\"TestName\",\"Value\":42}");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
        var config = (TestConfig)result!;
        Assert.That(config.Name, Is.EqualTo("TestName"));
        Assert.That(config.Value, Is.EqualTo(42));
    }

    [Test]
    public void Materialize_WhitespacePaddedValidJson_DeserializesCorrectly()
    {
        var result = DynamicConfigurationForm.Materialize(
            typeof(TestConfig),
            "  {\"Name\":\"Padded\",\"Value\":7}  ");

        Assert.That(result, Is.Not.Null);
        var config = (TestConfig)result!;
        Assert.That(config.Name, Is.EqualTo("Padded"));
        Assert.That(config.Value, Is.EqualTo(7));
    }

    [Test]
    public void Materialize_NullJsonLiteral_CreatesFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(typeof(TestConfig), "null");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
    }

    [Test]
    public void Materialize_MalformedJson_FallsBackToFreshInstance()
    {
        var result = DynamicConfigurationForm.Materialize(
            typeof(TestConfig),
            "this is not valid json {");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<TestConfig>());
    }

    [Test]
    public void Materialize_NoParameterlessConstructor_NullJson_Throws()
    {
        Assert.That(
            () => DynamicConfigurationForm.Materialize(typeof(NoDefaultCtorConfig), null),
            Throws.Exception);
    }

    [Test]
    public void Materialize_NoParameterlessConstructor_MalformedJsonFallback_ReturnsNull()
    {
        var result = DynamicConfigurationForm.Materialize(
            typeof(NoDefaultCtorConfig), "bad json");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Render_ReturnsPartialViewResultWithCorrectViewName()
    {
        var model = new TestConfig { Name = "ModelName" };

        var result = DynamicConfigurationForm.Render(model);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<PartialViewResult>());
        Assert.That(result.ViewName, Is.EqualTo("~/Views/AdminShared/_DynamicForm.cshtml"));
    }

    [Test]
    public void Render_SetsModelOnViewData()
    {
        var model = new TestConfig { Name = "ModelData", Value = 99 };

        var result = DynamicConfigurationForm.Render(model);

        Assert.That(result.ViewData, Is.Not.Null);
        Assert.That(result.ViewData!.Model, Is.SameAs(model));
    }

    [Test]
    public void Render_NullModel_Works()
    {
        var result = DynamicConfigurationForm.Render(null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ViewName, Is.EqualTo("~/Views/AdminShared/_DynamicForm.cshtml"));
        Assert.That(result.ViewData!.Model, Is.Null);
    }
}
