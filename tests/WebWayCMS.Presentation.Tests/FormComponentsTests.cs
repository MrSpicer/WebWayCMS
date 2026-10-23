using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ClearExtensions;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Pages;
using WebWayCMS.Services;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormFieldViewComponentBaseTests
{
    private static FormFieldContext CreateContext(FormFieldMode mode = FormFieldMode.Write) => new()
    {
        Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
        Value = "test",
        Mode = mode,
        InputName = "TestProp",
        ElementId = "TestProp",
        JsonBound = false
    };

    [Test]
    public void Invoke_WriteMode_ReturnsWriteViewWithFieldModel()
    {
        var component = new FormText();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateContext(FormFieldMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_ReadMode_ReturnsReadViewWithFieldModel()
    {
        var component = new FormText();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_FieldViewName_RendersThatVariant()
    {
        var component = new FormText();
        var harness = FormComponentHarness.Attach(component);
        harness.Hit("FormText", FormFieldRenderMode.AdminWrite, "Compact", "adminwrite.Compact");
        var context = CreateContext(FormFieldMode.Write);
        context.Property.FieldViewName = "Compact";

        var result = component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.Compact"));
    }

    [Test]
    public void Invoke_FieldViewName_MissingVariant_FallsBackToModeDefault()
    {
        var component = new FormText();
        FormComponentHarness.Attach(component);
        var context = CreateContext(FormFieldMode.Read);
        context.Property.FieldViewName = "Compact";

        var result = component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
    }

    [Test]
    public void Invoke_PublicPath_WriteMode_RendersPublicWriteView()
    {
        var component = new FormText();
        var harness = FormComponentHarness.Attach(component, path: "/contact");
        harness.Hit("FormText", FormFieldRenderMode.Write, "default", "write.default");

        var result = component.Invoke(CreateContext(FormFieldMode.Write));

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("write.default"));
    }

    [Test]
    public void Invoke_PublicPath_ReadMode_RendersPublicReadView()
    {
        var component = new FormText();
        var harness = FormComponentHarness.Attach(component, path: "/contact");
        harness.Hit("FormText", FormFieldRenderMode.Read, "default", "read.default");

        var result = component.Invoke(CreateContext(FormFieldMode.Read));

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("read.default"));
    }

    [Test]
    public void Invoke_PathSharingAdminPrefix_IsPublic_FallsBackToAdminView()
    {
        var component = new FormText();
        var harness = FormComponentHarness.Attach(component, path: "/wadmin-guide");

        var result = component.Invoke(CreateContext(FormFieldMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
            harness.Discovery.Received().TryResolveView("FormText", FormFieldRenderMode.Write, "default", out Arg.Any<string?>());
        });
    }

    [Test]
    public void Invoke_NoViewOnChain_Throws()
    {
        var component = new FormText();
        var harness = FormComponentHarness.Attach(component);
        harness.Discovery.ClearSubstitute();
        var context = CreateContext(FormFieldMode.Write);
        context.Property.FieldViewName = "Compact";

        var ex = Assert.Throws<InvalidOperationException>(() => component.Invoke(context));

        Assert.That(ex!.Message, Does.Contain("FormText").And.Contain("adminwrite").And.Contain("Compact"));
    }

    [Test]
    public void Invoke_NoViewOnChain_DefaultVariant_NamesDefaultInMessage()
    {
        var component = new FormText();
        var harness = FormComponentHarness.Attach(component);
        harness.Discovery.ClearSubstitute();

        var ex = Assert.Throws<InvalidOperationException>(() => component.Invoke(CreateContext(FormFieldMode.Read)));

        Assert.That(ex!.Message, Does.Contain("adminread").And.Contain("variant 'default'"));
    }
}

[TestFixture]
public class FormHiddenTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormHidden();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormRichTextTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormRichText();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormCheckboxTests
{
    [Test]
    public void Invoke_ReadMode_ReturnsReadView()
    {
        var component = new FormCheckbox();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = true,
            Mode = FormFieldMode.Read,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
    }
}

[TestFixture]
public class FormDateTests
{
    [Test]
    public void Invoke_FieldViewName_ReturnsThatView()
    {
        var component = new FormDate();
        var harness = FormComponentHarness.Attach(component);
        harness.Hit("FormDate", FormFieldRenderMode.AdminWrite, "SpecialDateView", "adminwrite.SpecialDateView");

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop", FieldViewName = "SpecialDateView" },
            Value = "2024-01-01",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.SpecialDateView"));
    }
}

[TestFixture]
public class FormDateTimeTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormDateTime();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "2024-01-01T12:00:00",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormNumberTests
{
    [Test]
    public void Invoke_ReadMode_ReturnsReadView()
    {
        var component = new FormNumber();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = 42,
            Mode = FormFieldMode.Read,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
    }
}

[TestFixture]
public class FormEmailTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormEmail();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test@example.com",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormUrlTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormUrl();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "https://example.com",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormColorTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormColor();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "#ff0000",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormGuidTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormGuid();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = Guid.NewGuid(),
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

[TestFixture]
public class FormTextAreaTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormTextArea();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "multiline\ntext",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
    }
}

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

[TestFixture]
public class FormEntityPickerTests
{
    [Test]
    public void Invoke_WithRegistry_ExposesResolvedEndpointInViewData()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("faqs");
        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler("faqs").Returns(handler);

        var component = new FormEntityPicker();
        FormComponentHarness.Attach(component, configure: services =>
        {
            services.AddSingleton(registry);
            services.AddSingleton(Substitute.For<ICompositeViewEngine>());
        });

        var context = new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "FaqId", Label = "FAQ", EntityType = "faqs" },
            Value = null,
            Mode = FormFieldMode.Write,
            InputName = "FaqId",
            ElementId = "FaqId",
            JsonBound = true
        };

        component.Invoke(context);

        Assert.That(component.ViewData[FormEntityPicker.EndpointViewDataKey], Is.EqualTo("/wadmin/faqs/api/list"));
    }

    [Test]
    public void Invoke_WithoutRegistry_EndpointIsNull()
    {
        var component = new FormEntityPicker();
        FormComponentHarness.Attach(component, configure: services =>
            services.AddSingleton(Substitute.For<ICompositeViewEngine>()));

        component.Invoke(CreateEntityPickerContext());

        Assert.Multiple(() =>
        {
            Assert.That(component.ViewData.ContainsKey(FormEntityPicker.EndpointViewDataKey), Is.True);
            Assert.That(component.ViewData[FormEntityPicker.EndpointViewDataKey], Is.Null);
        });
    }

    private static FormFieldContext CreateEntityPickerContext(
        FormFieldMode mode = FormFieldMode.Write) => new()
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = Guid.NewGuid(),
            Mode = mode,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        };

    [Test]
    public void Invoke_ReadMode_ReturnsReadViewWithFieldModel()
    {
        var component = new FormEntityPicker();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateEntityPickerContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_ReturnsWriteViewWithFieldModel()
    {
        var component = new FormEntityPicker();
        FormComponentHarness.Attach(component);

        var result = component.Invoke(CreateEntityPickerContext(FormFieldMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.default"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_FieldViewName_ReturnsThatView()
    {
        var component = new FormEntityPicker();
        var harness = FormComponentHarness.Attach(component);
        harness.Hit("FormEntityPicker", FormFieldRenderMode.AdminWrite, "CustomEntityPickerView", "adminwrite.CustomEntityPickerView");
        var context = CreateEntityPickerContext();
        context.Property.FieldViewName = "CustomEntityPickerView";

        var result = component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminwrite.CustomEntityPickerView"));
    }

    [Test]
    public void Invoke_FieldViewName_ReadMode_RendersThatVariant()
    {
        var component = new FormEntityPicker();
        var harness = FormComponentHarness.Attach(component);
        harness.Hit("FormEntityPicker", FormFieldRenderMode.AdminRead, "CustomReadView", "adminread.CustomReadView");
        var context = CreateEntityPickerContext(FormFieldMode.Read);
        context.Property.FieldViewName = "CustomReadView";

        var result = component.Invoke(context);

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("adminread.CustomReadView"));
    }
}
