using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class EntityPickerEndpointRelationTests
{
    [Test]
    public void ResolveHandler_NullOrBlankOrNoRegistry_ReturnsNull()
    {
        var registry = Substitute.For<IAdminHandlerRegistry>();

        Assert.Multiple(() =>
        {
            Assert.That(EntityPickerEndpoint.ResolveHandler(null, registry), Is.Null);
            Assert.That(EntityPickerEndpoint.ResolveHandler("  ", registry), Is.Null);
            Assert.That(EntityPickerEndpoint.ResolveHandler("faqs", null), Is.Null);
        });
    }

    [Test]
    public void ResolveHandler_ExactThenPlural()
    {
        var exact = Substitute.For<IAdminCrudHandler>();
        exact.ContentType.Returns("faqs");
        var plural = Substitute.For<IAdminCrudHandler>();
        plural.ContentType.Returns("articlelists");

        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.GetHandler("faqs").Returns(exact);
        registry.GetHandler("articlelist").Returns((IAdminCrudHandler?)null);
        registry.GetHandler("articlelists").Returns(plural);
        registry.GetHandler("nothing").Returns((IAdminCrudHandler?)null);
        registry.GetHandler("nothings").Returns((IAdminCrudHandler?)null);

        Assert.Multiple(() =>
        {
            Assert.That(EntityPickerEndpoint.ResolveHandler("faqs", registry), Is.SameAs(exact));
            Assert.That(EntityPickerEndpoint.ResolveHandler("articlelist", registry), Is.SameAs(plural));
            Assert.That(EntityPickerEndpoint.ResolveContentType("faqs", registry), Is.EqualTo("faqs"));
            Assert.That(EntityPickerEndpoint.ResolveContentType("nothing", registry), Is.Null);
        });
    }
}
