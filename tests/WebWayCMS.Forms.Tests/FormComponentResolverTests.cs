using NSubstitute;
using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class FormComponentResolverTests
{
    private IFormComponentRegistry _registry = null!;
    private FormComponentResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IFormComponentRegistry>();
        _resolver = new FormComponentResolver(_registry);
    }

    [Test]
    public void Constructor_NullRegistry_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new FormComponentResolver(null!));
        Assert.That(ex!.ParamName, Is.EqualTo("registry"));
    }

    [Test]
    public void Resolve_NullProp_ReturnsNull()
    {
        Assert.That(_resolver.Resolve(null!), Is.Null);
    }

    [Test]
    public void Resolve_ExplicitFormComponent_ReturnsByName()
    {
        var customComponent = new FormComponentInfo { Name = "Custom", ViewComponentName = "Custom" };
        _registry.GetByName("Custom").Returns(customComponent);
        var prop = new FormPropertyInfo { FormComponent = "Custom", PropertyType = typeof(string) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(customComponent));
    }

    [Test]
    public void Resolve_ExplicitFormComponentNotFound_FallsThroughToEditorType()
    {
        var editorComponent = new FormComponentInfo { Name = "Number", EditorTypeAlias = EditorType.Number };
        _registry.GetByName("Custom").Returns((FormComponentInfo?)null);
        _registry.GetForEditorType(EditorType.Number).Returns(editorComponent);
        var prop = new FormPropertyInfo
        {
            FormComponent = "Custom",
            EditorType = EditorType.Number,
            PropertyType = typeof(int)
        };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(editorComponent));
    }

    [Test]
    public void Resolve_EditorTypeMatch_ReturnsForEditorType()
    {
        var textComponent = new FormComponentInfo { Name = "Textarea", EditorTypeAlias = EditorType.TextArea };
        _registry.GetForEditorType(EditorType.TextArea).Returns(textComponent);
        var prop = new FormPropertyInfo { EditorType = EditorType.TextArea, PropertyType = typeof(string) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(textComponent));
    }

    [Test]
    public void Resolve_EditorTypeNotFound_FallsThroughToDefaultForType()
    {
        var stringComponent = new FormComponentInfo { Name = "Text", IsDefaultForType = true };
        _registry.GetForEditorType(EditorType.TextArea).Returns((FormComponentInfo?)null);
        _registry.GetDefaultFor(typeof(string)).Returns(stringComponent);
        var prop = new FormPropertyInfo { EditorType = EditorType.TextArea, PropertyType = typeof(string) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(stringComponent));
    }

    [Test]
    public void Resolve_ClrDefault_ReturnsDefaultFor()
    {
        var intComponent = new FormComponentInfo { Name = "Number", IsDefaultForType = true };
        _registry.GetDefaultFor(typeof(int)).Returns(intComponent);
        var prop = new FormPropertyInfo { PropertyType = typeof(int) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(intComponent));
    }

    [Test]
    public void Resolve_NullableUnwrapsToUnderlyingType()
    {
        var intComponent = new FormComponentInfo { Name = "Number", IsDefaultForType = true };
        _registry.GetDefaultFor(typeof(int)).Returns(intComponent);
        var prop = new FormPropertyInfo { PropertyType = typeof(int?) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(intComponent));
    }

    [Test]
    public void Resolve_TextFallback_ReturnsTextComponent()
    {
        var textComponent = new FormComponentInfo { Name = "Text", ViewComponentName = "Text" };
        _registry.GetByName("Text").Returns(textComponent);
        var prop = new FormPropertyInfo { PropertyType = typeof(string) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.SameAs(textComponent));
    }

    [Test]
    public void Resolve_TextFallbackMissing_ReturnsNull()
    {
        _registry.GetByName("Text").Returns((FormComponentInfo?)null);
        var prop = new FormPropertyInfo { PropertyType = typeof(string) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Resolve_NoMatch_ReturnsNull()
    {
        _registry.GetByName(Arg.Any<string>()).Returns((FormComponentInfo?)null);
        _registry.GetForEditorType(Arg.Any<EditorType>()).Returns((FormComponentInfo?)null);
        _registry.GetDefaultFor(Arg.Any<Type>()).Returns((FormComponentInfo?)null);
        var prop = new FormPropertyInfo { PropertyType = typeof(object) };

        var result = _resolver.Resolve(prop);

        Assert.That(result, Is.Null);
    }
}

[TestFixture]
public class CMSFormComponentAttributeTests
{
    [Test]
    public void Constructor_WithName_SetsName()
    {
        var attr = new CMSFormComponentAttribute("RichText");

        Assert.That(attr.Name, Is.EqualTo("RichText"));
    }

    [Test]
    public void Constructor_WithName_DefaultsOtherProperties()
    {
        var attr = new CMSFormComponentAttribute("Text");

        Assert.Multiple(() =>
        {
            Assert.That(attr.DataTypes, Is.Empty);
            Assert.That(attr.IsDefaultForType, Is.False);
            Assert.That(attr.EditorType, Is.Null);
            Assert.That(attr.DisplayName, Is.Empty);
            Assert.That(attr.Description, Is.Empty);
            Assert.That(attr.Category, Is.EqualTo("General"));
            Assert.That(attr.IconClass, Is.Empty);
            Assert.That(attr.Order, Is.EqualTo(0));
            Assert.That(attr.WriteViewName, Is.EqualTo("Write"));
            Assert.That(attr.ReadViewName, Is.EqualTo("Read"));
        });
    }

    [Test]
    public void Constructor_Parameterless_SetsNameToEmpty()
    {
        var attr = new CMSFormComponentAttribute();

        Assert.That(attr.Name, Is.Empty);
    }

    [Test]
    public void Constructor_Parameterless_DefaultsOtherProperties()
    {
        var attr = new CMSFormComponentAttribute();

        Assert.Multiple(() =>
        {
            Assert.That(attr.DataTypes, Is.Empty);
            Assert.That(attr.IsDefaultForType, Is.False);
            Assert.That(attr.EditorType, Is.Null);
        });
    }

    [Test]
    public void SettableProperties_CanBeSet()
    {
        var attr = new CMSFormComponentAttribute("Custom")
        {
            DataTypes = new[] { typeof(string), typeof(int) },
            IsDefaultForType = true,
            EditorType = EditorType.Text,
            DisplayName = "My Component",
            Description = "A custom component",
            Category = "Special",
            IconClass = "fa-star",
            Order = 5,
            WriteViewName = "Edit",
            ReadViewName = "View"
        };

        Assert.Multiple(() =>
        {
            Assert.That(attr.Name, Is.EqualTo("Custom"));
            Assert.That(attr.DataTypes, Is.EquivalentTo(new[] { typeof(string), typeof(int) }));
            Assert.That(attr.IsDefaultForType, Is.True);
            Assert.That(attr.EditorType, Is.EqualTo(EditorType.Text));
            Assert.That(attr.DisplayName, Is.EqualTo("My Component"));
            Assert.That(attr.Description, Is.EqualTo("A custom component"));
            Assert.That(attr.Category, Is.EqualTo("Special"));
            Assert.That(attr.IconClass, Is.EqualTo("fa-star"));
            Assert.That(attr.Order, Is.EqualTo(5));
            Assert.That(attr.WriteViewName, Is.EqualTo("Edit"));
            Assert.That(attr.ReadViewName, Is.EqualTo("View"));
        });
    }
}
