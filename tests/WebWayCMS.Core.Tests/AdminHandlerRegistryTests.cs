using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminHandlerRegistryTests
{
    [Test]
    public void GetHandler_ReturnsRegisteredHandlerCaseInsensitively()
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns("pages");
        var registry = new AdminHandlerRegistry(new[] { handler });

        Assert.Multiple(() =>
        {
            Assert.That(registry.GetHandler("pages"), Is.SameAs(handler));
            Assert.That(registry.GetHandler("PAGES"), Is.SameAs(handler));
            Assert.That(registry.GetHandler("missing"), Is.Null);
        });
    }

    [Test]
    public void All_ReturnsHandlersInRegistrationOrder()
    {
        var first = Substitute.For<IAdminCrudHandler>();
        first.ContentType.Returns("pages");
        var second = Substitute.For<IAdminCrudHandler>();
        second.ContentType.Returns("faqs");

        var registry = new AdminHandlerRegistry(new[] { first, second });

        Assert.That(registry.All, Is.EqualTo(new[] { first, second }));
    }
}
