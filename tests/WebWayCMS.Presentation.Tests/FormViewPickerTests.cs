using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using WebWayCMS.Services;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormViewPickerTests
{
    private IViewDiscoveryService _viewDiscoveryService = null!;
    private FormViewPicker _component = null!;
    private FormComponentHarness.Context _harness = null!;

    [SetUp]
    public void SetUp()
    {
        _viewDiscoveryService = Substitute.For<IViewDiscoveryService>();
        _component = new FormViewPicker(_viewDiscoveryService);
        _harness = FormComponentHarness.Attach(_component);
    }

    private static FormFieldContext CreateViewPickerContext(
        string? viewComponentName = "TestComponent",
        FormFieldMode mode = FormFieldMode.Write,
        object? value = null,
        WebWayCMS.Attributes.WidgetRenderMode? renderMode = null,
        string? componentName = null) => new()
    {
        Property = new FormPropertyInfo
        {
            Name = "TestProp",
            Label = "Test Prop",
            ViewComponentName = viewComponentName,
            RenderMode = renderMode ?? WebWayCMS.Attributes.WidgetRenderMode.View
        },
        ComponentName = componentName,
        RenderMode = renderMode ?? WebWayCMS.Attributes.WidgetRenderMode.View,
        Value = value,
        Mode = mode,
        InputName = "TestProp",
        ElementId = "TestProp",
        JsonBound = false
    };

    [Test]
    public void Constructor_NullViewDiscoveryService_Throws()
    {
        Assert.That(() => new FormViewPicker(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Invoke_ReadMode_ReturnsReadViewWithFieldModel()
    {
        var result = _component.Invoke(CreateViewPickerContext(mode: FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_ViewComponentNameSet_PopulatesOptionsForTheFieldRenderMode()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "Default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext());

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
            // "Default" is covered by the empty "-- Default --" option, so it is not listed again.
            Assert.That(model.Options, Has.Count.EqualTo(1));
            Assert.That(model.Options.ContainsKey("Default"), Is.False);
            Assert.That(model.Options["Alternate"], Is.EqualTo("Alternate"));
        });
    }

    [Test]
    public void Invoke_WriteMode_EditRenderMode_ListsOnlyThatModesViews()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.Edit)
            .Returns(new List<string> { "default", "Compact" });

        var result = _component.Invoke(CreateViewPickerContext(renderMode: WebWayCMS.Attributes.WidgetRenderMode.Edit));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(1));
            Assert.That(model.Options["Compact"], Is.EqualTo("Compact"));
        });
        _viewDiscoveryService.Received(1).GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.Edit);
        _viewDiscoveryService.DidNotReceive().GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View);
    }

    [Test]
    public void Invoke_WriteMode_NoViewComponentName_FallsBackToFormComponentName()
    {
        _viewDiscoveryService.GetAvailableViews("MyWidget", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "Default", "Wide" });

        var result = _component.Invoke(CreateViewPickerContext(viewComponentName: null, componentName: "MyWidget"));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options.Keys, Is.EquivalentTo(new[] { "Wide" }));
    }

    [Test]
    public void Invoke_WriteMode_ViewComponentNameEmpty_RendersNothing()
    {
        var result = _component.Invoke(CreateViewPickerContext(viewComponentName: ""));

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public void Invoke_WriteMode_ViewComponentNameWhitespace_RendersNothing()
    {
        var result = _component.Invoke(CreateViewPickerContext(viewComponentName: "   "));

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public void Invoke_WriteMode_GetAvailableViewsReturnsEmpty_RendersNothing()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string>());

        var result = _component.Invoke(CreateViewPickerContext());

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public void Invoke_FieldViewName_ReturnsThatView()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "Default" });
        _harness.Hit("FormViewPicker", FormFieldRenderMode.AdminWrite, "CustomViewPickerView", "adminwrite.CustomViewPickerView");
        var context = CreateViewPickerContext();
        context.Property.FieldViewName = "CustomViewPickerView";

        var result = _component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.CustomViewPickerView"));
    }

    [Test]
    public void Invoke_WriteMode_ValueNullOrWhitespace_NotAdded()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "Default" });

        var nullResult = _component.Invoke(CreateViewPickerContext(value: null));
        var nullModel = (DropdownViewModel)ViewComponentHarness.Model(nullResult)!;

        var whitespaceResult = _component.Invoke(CreateViewPickerContext(value: "   "));
        var whitespaceModel = (DropdownViewModel)ViewComponentHarness.Model(whitespaceResult)!;

        // Only the default view exists: the picker still renders (with just "-- Default --").
        Assert.Multiple(() =>
        {
            Assert.That(nullModel.Options, Is.Empty);
            Assert.That(whitespaceModel.Options, Is.Empty);
        });
    }

    [Test]
    public void Invoke_WriteMode_ValueAlreadyDiscovered_NotDuplicated()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "Default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext(value: "Alternate"));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(1));
            Assert.That(model.Options["Alternate"], Is.EqualTo("Alternate"));
        });
    }

    [Test]
    public void Invoke_WriteMode_StoredDefaultValue_KeptSelectableOnce()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext(value: "Default"));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options.Keys, Is.EquivalentTo(new[] { "Alternate", "Default" }));
    }

    [Test]
    public void Invoke_WriteMode_ValueAbsent_Added()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent", WebWayCMS.Attributes.WidgetRenderMode.View)
            .Returns(new List<string> { "Default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext(value: "Renamed"));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(2));
            Assert.That(model.Options["Alternate"], Is.EqualTo("Alternate"));
            Assert.That(model.Options["Renamed"], Is.EqualTo("Renamed"));
        });
    }
}
