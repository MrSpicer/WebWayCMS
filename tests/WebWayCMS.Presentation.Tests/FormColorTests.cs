using NUnit.Framework;

using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

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
