using NUnit.Framework;

using WebWayCMS.Forms;
using WebWayCMS.ViewComponents.Forms;

namespace WebWayCMS.Presentation.Tests;

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
