using NSubstitute;
using NSubstitute.ClearExtensions;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
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
