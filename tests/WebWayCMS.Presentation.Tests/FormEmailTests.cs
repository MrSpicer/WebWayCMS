using NUnit.Framework;

using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

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
