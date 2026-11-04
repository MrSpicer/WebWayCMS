using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminRouteReservationTests
{
    private IAdminHandlerRegistry _registry = null!;
    private IServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = Substitute.For<IAdminHandlerRegistry>();
        _registry.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);
        _registry.GetHandler("pages").Returns(Substitute.For<IAdminCrudHandler>());
        _services = new ServiceCollection().AddSingleton(_registry).BuildServiceProvider();
    }

    private bool Match(object? value, RouteDirection direction = RouteDirection.IncomingRequest, bool withContext = true)
    {
        var context = withContext ? new DefaultHttpContext { RequestServices = _services } : null;
        var values = new RouteValueDictionary();
        if (value != null)
            values["contentType"] = value;
        return new AdminContentTypeConstraint().Match(context, null, "contentType", values, direction);
    }

    [Test]
    public void Constraint_MatchesOnlyRegisteredContentTypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AdminContentTypeConstraint.Name, Is.EqualTo("admincontenttype"));
            Assert.That(Match("pages"), Is.True);
            Assert.That(Match("users"), Is.False);
            Assert.That(Match(" "), Is.False);
            Assert.That(Match(null), Is.False);
            Assert.That(Match(42), Is.False);
            Assert.That(Match("pages", withContext: false), Is.False);
            Assert.That(Match("users", RouteDirection.UrlGeneration), Is.True);
        });
    }

    [Test]
    public void Constraint_NoRegistry_DoesNotMatch()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var values = new RouteValueDictionary { ["contentType"] = "pages" };

        Assert.That(new AdminContentTypeConstraint().Match(context, null, "contentType", values, RouteDirection.IncomingRequest), Is.False);
    }

    [TestCase("/wadmin/pages", true)]
    [TestCase("/wadmin/pages/{x}", true)]
    [TestCase("/WADMIN/pages", true)]
    [TestCase("/wadmin/users", false)]
    [TestCase("/wadmin", false)]
    [TestCase("/pages", false)]
    public void Reservations_RefuseRoutesUnderAnAdminContentScreen(string pattern, bool refused)
    {
        var reservations = new AdminContentRouteReservations(_services);

        var conflict = reservations.GetConflict(pattern);

        Assert.That(conflict != null, Is.EqualTo(refused));
        if (refused)
            Assert.That(conflict, Does.Contain("collides with the admin screen for 'pages'"));
    }

    [Test]
    public void Reservations_NullServices_Throws()
    {
        Assert.That(() => new AdminContentRouteReservations(null!), Throws.ArgumentNullException);
    }
}
