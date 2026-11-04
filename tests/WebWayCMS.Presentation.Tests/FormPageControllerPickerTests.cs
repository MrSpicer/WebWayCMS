using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using WebWayCMS.Pages;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormPageControllerPickerTests
{
    private IPageControllerRegistry _pageControllerRegistry = null!;
    private FormPageControllerPicker _component = null!;
    private FormComponentHarness.Context _harness = null!;

    [SetUp]
    public void SetUp()
    {
        _pageControllerRegistry = Substitute.For<IPageControllerRegistry>();
        _component = new FormPageControllerPicker(_pageControllerRegistry);
        _harness = FormComponentHarness.Attach(_component);
    }

    private static FormFieldContext CreatePageControllerPickerContext(
        FormFieldMode mode = FormFieldMode.Write) => new()
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test",
            Mode = mode,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        };

    [Test]
    public void Constructor_NullPageControllerRegistry_Throws()
    {
        Assert.That(() => new FormPageControllerPicker(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Invoke_ReadMode_ReturnsReadViewWithFieldModel()
    {
        var result = _component.Invoke(CreatePageControllerPickerContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_PopulatesOptionsFromRegistry()
    {
        _pageControllerRegistry.GetAllControllers().Returns(new List<PageControllerInfo>
        {
            new() { Name = "Blog", DisplayName = "Blog Controller", Description = "" },
            new() { Name = "Home", DisplayName = "Home Controller", Description = "" }
        });

        var result = _component.Invoke(CreatePageControllerPickerContext());

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
            Assert.That(model.Options, Has.Count.EqualTo(2));
            Assert.That(model.Options["Blog"], Is.EqualTo("Blog Controller"));
            Assert.That(model.Options["Home"], Is.EqualTo("Home Controller"));
        });
    }

    [Test]
    public void Invoke_DescriptionNotEmpty_AppendedToLabel()
    {
        _pageControllerRegistry.GetAllControllers().Returns(new List<PageControllerInfo>
        {
            new() { Name = "Blog", DisplayName = "Blog", Description = "A blog page type" }
        });

        var result = _component.Invoke(CreatePageControllerPickerContext());

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options["Blog"], Is.EqualTo("Blog - A blog page type"));
    }

    [Test]
    public void Invoke_EmptyControllers_ReturnsEmptyOptions()
    {
        _pageControllerRegistry.GetAllControllers().Returns(new List<PageControllerInfo>());

        var result = _component.Invoke(CreatePageControllerPickerContext());

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options, Is.Empty);
    }

    [Test]
    public void Invoke_FieldViewName_ReturnsThatView()
    {
        _pageControllerRegistry.GetAllControllers().Returns(new List<PageControllerInfo>
        {
            new() { Name = "Blog", DisplayName = "Blog" }
        });
        _harness.Hit("FormPageControllerPicker", FormFieldRenderMode.AdminWrite, "CustomPagePickerView", "adminwrite.CustomPagePickerView");
        var context = CreatePageControllerPickerContext();
        context.Property.FieldViewName = "CustomPagePickerView";

        var result = _component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.CustomPagePickerView"));
    }
}
