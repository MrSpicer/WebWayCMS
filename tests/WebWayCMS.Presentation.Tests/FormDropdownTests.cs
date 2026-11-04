using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormDropdownTests
{
    private static FormFieldContext CreateDropdownContext(
        FormFieldMode mode = FormFieldMode.Write,
        Dictionary<string, string>? dropdownOptions = null,
        Type? propertyType = null) => new()
        {
            Property = new FormPropertyInfo
            {
                Name = "TestProp",
                Label = "Test Prop",
                DropdownOptions = dropdownOptions ?? new Dictionary<string, string>(),
                PropertyType = propertyType ?? typeof(string)
            },
            Value = "test",
            Mode = mode,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        };

    [Test]
    public void Invoke_ReadMode_ReturnsReadViewWithFieldModel()
    {
        var component = new FormDropdown();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_ReturnsDropdownViewModel()
    {
        var component = new FormDropdown();
        FormComponentHarness.Attach(component);

        var options = new Dictionary<string, string> { ["val1"] = "Label 1", ["val2"] = "Label 2" };
        var result = component.Invoke(CreateDropdownContext(dropdownOptions: options));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
            Assert.That(model.Options, Is.EqualTo(options));
            Assert.That(model.Field, Is.Not.Null);
        });
    }

    [Test]
    public void Invoke_EmptyOptions_EnumPropertyType_AutoPopulatesFromEnum()
    {
        var component = new FormDropdown();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(propertyType: typeof(System.DayOfWeek)));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(7));
            Assert.That(model.Options["0"], Is.EqualTo("Sunday"));
            Assert.That(model.Options["1"], Is.EqualTo("Monday"));
        });
    }

    [Test]
    public void Invoke_EmptyOptions_NullableEnumPropertyType_AutoPopulatesFromUnderlyingEnum()
    {
        var component = new FormDropdown();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(propertyType: typeof(System.DayOfWeek?)));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(7));
            Assert.That(model.Options["0"], Is.EqualTo("Sunday"));
        });
    }

    [Test]
    public void Invoke_EmptyOptions_NonEnumPropertyType_ReturnsEmptyOptions()
    {
        var component = new FormDropdown();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(propertyType: typeof(string)));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options, Is.Empty);
    }

    [Test]
    public void Invoke_FieldViewName_RendersThatVariant()
    {
        var component = new FormDropdown();
        var harness = FormComponentHarness.Attach(component);
        harness.Hit("FormDropdown", FormFieldRenderMode.AdminWrite, "CustomDropdownView", "adminwrite.CustomDropdownView");
        var context = CreateDropdownContext();
        context.Property.FieldViewName = "CustomDropdownView";

        var result = component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.CustomDropdownView"));
    }

    [Test]
    public void Invoke_FieldViewName_ReadMode_RendersThatVariant()
    {
        var component = new FormDropdown();
        var harness = FormComponentHarness.Attach(component);
        harness.Hit("FormDropdown", FormFieldRenderMode.AdminRead, "CustomReadView", "adminread.CustomReadView");
        var context = CreateDropdownContext(FormFieldMode.Read);
        context.Property.FieldViewName = "CustomReadView";

        var result = component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.CustomReadView"));
    }
}
