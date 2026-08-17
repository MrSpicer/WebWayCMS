using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

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
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateContext(FormFieldMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_ReadMode_ReturnsReadViewWithFieldModel()
    {
        var component = new FormText();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_CustomViewName_ReturnsThatViewName()
    {
        var component = new FormText();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateContext(FormFieldMode.Write), "CustomView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomView"));
    }

    [Test]
    public void Invoke_CustomViewName_OverridesReadMode()
    {
        var component = new FormText();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateContext(FormFieldMode.Read), "CustomView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomView"));
    }
}

[TestFixture]
public class FormHiddenTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormHidden();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormRichTextTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormRichText();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormCheckboxTests
{
    [Test]
    public void Invoke_ReadMode_ReturnsReadView()
    {
        var component = new FormCheckbox();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = true,
            Mode = FormFieldMode.Read,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
    }
}

[TestFixture]
public class FormDateTests
{
    [Test]
    public void Invoke_CustomViewName_ReturnsThatView()
    {
        var component = new FormDate();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "2024-01-01",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        }, "SpecialDateView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("SpecialDateView"));
    }
}

[TestFixture]
public class FormDateTimeTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormDateTime();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "2024-01-01T12:00:00",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormNumberTests
{
    [Test]
    public void Invoke_ReadMode_ReturnsReadView()
    {
        var component = new FormNumber();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = 42,
            Mode = FormFieldMode.Read,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
    }
}

[TestFixture]
public class FormEmailTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormEmail();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "test@example.com",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormUrlTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormUrl();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "https://example.com",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormColorTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormColor();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "#ff0000",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormGuidTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormGuid();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = Guid.NewGuid(),
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
    }
}

[TestFixture]
public class FormTextAreaTests
{
    [Test]
    public void Invoke_WriteMode_ReturnsWriteView()
    {
        var component = new FormTextArea();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(new FormFieldContext
        {
            Property = new FormPropertyInfo { Name = "TestProp", Label = "Test Prop" },
            Value = "multiline\ntext",
            Mode = FormFieldMode.Write,
            InputName = "TestProp",
            ElementId = "TestProp",
            JsonBound = false
        });

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
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
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_ReturnsDropdownViewModel()
    {
        var component = new FormDropdown();
        ViewComponentHarness.Attach(component);

        var options = new Dictionary<string, string> { ["val1"] = "Label 1", ["val2"] = "Label 2" };
        var result = component.Invoke(CreateDropdownContext(dropdownOptions: options));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
            Assert.That(model.Options, Is.EqualTo(options));
            Assert.That(model.Field, Is.Not.Null);
        });
    }

    [Test]
    public void Invoke_EmptyOptions_EnumPropertyType_AutoPopulatesFromEnum()
    {
        var component = new FormDropdown();
        ViewComponentHarness.Attach(component);

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
        ViewComponentHarness.Attach(component);

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
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(propertyType: typeof(string)));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options, Is.Empty);
    }

    [Test]
    public void Invoke_CustomViewName_OverridesViewName()
    {
        var component = new FormDropdown();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(), "CustomDropdownView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomDropdownView"));
    }

    [Test]
    public void Invoke_CustomViewName_ReadMode_OverridesViewName()
    {
        var component = new FormDropdown();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateDropdownContext(FormFieldMode.Read), "CustomReadView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomReadView"));
    }
}

[TestFixture]
public class FormViewPickerTests
{
    private IViewDiscoveryService _viewDiscoveryService = null!;
    private FormViewPicker _component = null!;

    [SetUp]
    public void SetUp()
    {
        _viewDiscoveryService = Substitute.For<IViewDiscoveryService>();
        _component = new FormViewPicker(_viewDiscoveryService);
        ViewComponentHarness.Attach(_component);
    }

    private static FormFieldContext CreateViewPickerContext(
        string viewComponentName = "TestComponent",
        FormFieldMode mode = FormFieldMode.Write,
        object? value = null) => new()
        {
            Property = new FormPropertyInfo
            {
                Name = "TestProp",
                Label = "Test Prop",
                ViewComponentName = viewComponentName
            },
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_ViewComponentNameSet_PopulatesOptions()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent")
            .Returns(new List<string> { "Default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext());

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
            Assert.That(model.Options, Has.Count.EqualTo(2));
            Assert.That(model.Options["Default"], Is.EqualTo("Default"));
            Assert.That(model.Options["Alternate"], Is.EqualTo("Alternate"));
        });
    }

    [Test]
    public void Invoke_WriteMode_ViewComponentNameEmpty_ReturnsEmptyOptions()
    {
        var result = _component.Invoke(CreateViewPickerContext(viewComponentName: ""));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options, Is.Empty);
    }

    [Test]
    public void Invoke_WriteMode_ViewComponentNameWhitespace_ReturnsEmptyOptions()
    {
        var result = _component.Invoke(CreateViewPickerContext(viewComponentName: "   "));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options, Is.Empty);
    }

    [Test]
    public void Invoke_WriteMode_GetAvailableViewsReturnsEmpty_ReturnsEmptyOptions()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent")
            .Returns(new List<string>());

        var result = _component.Invoke(CreateViewPickerContext());

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.That(model.Options, Is.Empty);
    }

    [Test]
    public void Invoke_CustomViewName_ReturnsThatView()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent")
            .Returns(new List<string> { "Default" });

        var result = _component.Invoke(CreateViewPickerContext(), "CustomViewPickerView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomViewPickerView"));
    }

    [Test]
    public void Invoke_WriteMode_ValueNullOrWhitespace_NotAdded()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent")
            .Returns(new List<string> { "Default" });

        var nullResult = _component.Invoke(CreateViewPickerContext(value: null));
        var nullModel = (DropdownViewModel)ViewComponentHarness.Model(nullResult)!;

        var whitespaceResult = _component.Invoke(CreateViewPickerContext(value: "   "));
        var whitespaceModel = (DropdownViewModel)ViewComponentHarness.Model(whitespaceResult)!;

        Assert.Multiple(() =>
        {
            Assert.That(nullModel.Options, Has.Count.EqualTo(1));
            Assert.That(nullModel.Options["Default"], Is.EqualTo("Default"));
            Assert.That(whitespaceModel.Options, Has.Count.EqualTo(1));
            Assert.That(whitespaceModel.Options["Default"], Is.EqualTo("Default"));
        });
    }

    [Test]
    public void Invoke_WriteMode_ValueAlreadyDiscovered_NotDuplicated()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent")
            .Returns(new List<string> { "Default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext(value: "Alternate"));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(2));
            Assert.That(model.Options["Alternate"], Is.EqualTo("Alternate"));
        });
    }

    [Test]
    public void Invoke_WriteMode_ValueAbsent_Added()
    {
        _viewDiscoveryService.GetAvailableViews("TestComponent")
            .Returns(new List<string> { "Default", "Alternate" });

        var result = _component.Invoke(CreateViewPickerContext(value: "Renamed"));

        var model = (DropdownViewModel)ViewComponentHarness.Model(result)!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Options, Has.Count.EqualTo(3));
            Assert.That(model.Options["Default"], Is.EqualTo("Default"));
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

    [SetUp]
    public void SetUp()
    {
        _pageControllerRegistry = Substitute.For<IPageControllerRegistry>();
        _component = new FormPageControllerPicker(_pageControllerRegistry);
        ViewComponentHarness.Attach(_component);
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
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
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
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
    public void Invoke_CustomViewName_ReturnsThatView()
    {
        _pageControllerRegistry.GetAllControllers().Returns(new List<PageControllerInfo>
        {
            new() { Name = "Blog", DisplayName = "Blog" }
        });

        var result = _component.Invoke(CreatePageControllerPickerContext(), "CustomPagePickerView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomPagePickerView"));
    }
}

[TestFixture]
public class FormEntityPickerTests
{
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
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateEntityPickerContext(FormFieldMode.Read));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Read"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_WriteMode_ReturnsWriteViewWithFieldModel()
    {
        var component = new FormEntityPicker();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateEntityPickerContext(FormFieldMode.Write));

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Write"));
            Assert.That(ViewComponentHarness.Model(result), Is.InstanceOf<FormFieldContext>());
        });
    }

    [Test]
    public void Invoke_CustomViewName_ReturnsThatView()
    {
        var component = new FormEntityPicker();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateEntityPickerContext(), "CustomEntityPickerView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomEntityPickerView"));
    }

    [Test]
    public void Invoke_CustomViewName_ReadMode_OverridesViewName()
    {
        var component = new FormEntityPicker();
        ViewComponentHarness.Attach(component);

        var result = component.Invoke(CreateEntityPickerContext(FormFieldMode.Read), "CustomReadView");

        Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("CustomReadView"));
    }
}
