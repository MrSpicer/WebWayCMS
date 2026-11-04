using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

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
