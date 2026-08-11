using NUnit.Framework;

using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests.Startup;

[TestFixture]
public class CmsWidgetRegistrationSeederTests
{
    [Test]
    public void GetWidgetComponentName_StripsViewComponentSuffix()
    {
        Assert.That(CmsWidgetRegistrationSeeder.GetWidgetComponentName(typeof(WidgetTestViewComponent)), Is.EqualTo("WidgetTest"));
    }

    [Test]
    public void GetWidgetComponentName_NoSuffix_ReturnsFullName()
    {
        Assert.That(CmsWidgetRegistrationSeeder.GetWidgetComponentName(typeof(PlainComponent)), Is.EqualTo("PlainComponent"));
    }
}
