using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

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
