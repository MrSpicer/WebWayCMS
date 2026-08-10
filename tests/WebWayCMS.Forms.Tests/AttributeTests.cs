using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class AttributeTests
{
    private sealed class Config { }

    [Test]
    public void FormPropertyAttribute_DefaultConstructor_UsesDefaults()
    {
        var attr = new FormPropertyAttribute();

        Assert.Multiple(() =>
        {
            Assert.That(attr.EditorType, Is.EqualTo(EditorType.Text));
            Assert.That(attr.Min, Is.NaN);
            Assert.That(attr.Max, Is.NaN);
            Assert.That(attr.MaxLength, Is.EqualTo(-1));
        });
    }

    [Test]
    public void FormPropertyAttribute_LabelConstructor_SetsLabelAndEditor()
    {
        var attr = new FormPropertyAttribute("My Label", EditorType.Number);

        Assert.Multiple(() =>
        {
            Assert.That(attr.Label, Is.EqualTo("My Label"));
            Assert.That(attr.EditorType, Is.EqualTo(EditorType.Number));
        });
    }

    [Test]
    public void ContentZoneComponentAttribute_DefaultConstructor_UsesDefaults()
    {
        var attr = new ContentZoneComponentAttribute();

        Assert.That(attr.Category, Is.EqualTo("General"));
    }

    [Test]
    public void ContentZoneComponentAttribute_NamedConstructor_SetsValues()
    {
        var attr = new ContentZoneComponentAttribute("Display", typeof(Config));

        Assert.Multiple(() =>
        {
            Assert.That(attr.DisplayName, Is.EqualTo("Display"));
            Assert.That(attr.ConfigurationType, Is.EqualTo(typeof(Config)));
        });
    }

    [Test]
    public void PageControllerAttribute_DefaultConstructor_UsesDefaults()
    {
        var attr = new PageControllerAttribute();

        Assert.That(attr.Category, Is.EqualTo("General"));
    }

    [Test]
    public void PageControllerAttribute_NamedConstructor_SetsValues()
    {
        var attr = new PageControllerAttribute("Display", typeof(Config));

        Assert.Multiple(() =>
        {
            Assert.That(attr.DisplayName, Is.EqualTo("Display"));
            Assert.That(attr.ConfigurationType, Is.EqualTo(typeof(Config)));
        });
    }

    [Test]
    public void CmsRouteAttribute_Constructor_SetsPattern()
    {
        var attr = new CmsRouteAttribute("/about");

        Assert.That(attr.Pattern, Is.EqualTo("/about"));
    }

    [Test]
    public void CmsRouteAttribute_Constructor_NullPattern_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new CmsRouteAttribute(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("pattern"));
    }

    [Test]
    public void CmsRouteAttribute_DefaultPropertyValues()
    {
        var attr = new CmsRouteAttribute("/test");

        Assert.Multiple(() =>
        {
            Assert.That(attr.Order, Is.EqualTo(0));
            Assert.That(attr.Action, Is.EqualTo("Index"));
            Assert.That(attr.Defaults, Is.Null);
            Assert.That(attr.Constraints, Is.Null);
            Assert.That(attr.DataTokens, Is.Null);
        });
    }

    [Test]
    public void CmsRouteAttribute_CustomPropertyValues()
    {
        var attr = new CmsRouteAttribute("/blog/{slug}")
        {
            Order = 10,
            Action = "Detail",
            Defaults = "{\"area\":\"Public\"}",
            Constraints = "{\"slug\":\"[a-z0-9-]+\"}",
            DataTokens = "{\"cache\":\"true\"}"
        };

        Assert.Multiple(() =>
        {
            Assert.That(attr.Pattern, Is.EqualTo("/blog/{slug}"));
            Assert.That(attr.Order, Is.EqualTo(10));
            Assert.That(attr.Action, Is.EqualTo("Detail"));
            Assert.That(attr.Defaults, Is.EqualTo("{\"area\":\"Public\"}"));
            Assert.That(attr.Constraints, Is.EqualTo("{\"slug\":\"[a-z0-9-]+\"}"));
            Assert.That(attr.DataTokens, Is.EqualTo("{\"cache\":\"true\"}"));
        });
    }

    [Test]
    public void CmsRouteAttribute_MultipleOnSameClass()
    {
        var type = typeof(MultiRouteController);
        var attrs = type.GetCustomAttributes(typeof(CmsRouteAttribute), false)
            .Cast<CmsRouteAttribute>().ToList();

        Assert.That(attrs, Has.Count.EqualTo(2));
        Assert.That(attrs[0].Pattern, Is.EqualTo("/first"));
        Assert.That(attrs[1].Pattern, Is.EqualTo("/second"));
    }

    [CmsRoute("/first")]
    [CmsRoute("/second")]
    private sealed class MultiRouteController
    {
    }
}