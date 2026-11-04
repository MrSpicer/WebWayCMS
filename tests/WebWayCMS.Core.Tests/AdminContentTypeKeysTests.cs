using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminContentTypeKeysTests
{
    private static IAdminHandlerRegistry Registry(params string[] keys)
    {
        // Built before Returns: NSubstitute can't configure one substitute inside another's Returns.
        var handlers = keys.Select(k =>
        {
            var handler = Substitute.For<IAdminCrudHandler>();
            handler.ContentType.Returns(k);
            return handler;
        }).ToList();
        var registry = Substitute.For<IAdminHandlerRegistry>();
        registry.All.Returns(handlers);
        return registry;
    }

    [Test]
    public void Contains_LoadsTheKeysOnceAndMatchesCaseInsensitively()
    {
        var registry = Registry("pages", "articles");
        var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        var keys = new AdminContentTypeKeys();

        Assert.Multiple(() =>
        {
            Assert.That(keys.Contains("pages", services), Is.True);
            Assert.That(keys.Contains("ARTICLES", services), Is.True);
            Assert.That(keys.Contains("users", services), Is.False);
        });
        _ = registry.Received(1).All;
    }

    [Test]
    public void Contains_NoRegistry_IsFalseAndLoadsLater()
    {
        var keys = new AdminContentTypeKeys();
        var empty = new ServiceCollection().BuildServiceProvider();
        var withRegistry = new ServiceCollection().AddSingleton(Registry("pages")).BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(keys.Contains("pages", empty), Is.False);
            Assert.That(keys.Contains("pages", withRegistry), Is.True);
            Assert.That(() => keys.Contains("pages", null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Constraint_WithKeys_NeverAsksTheScopedRegistryForAHandler()
    {
        var registry = Registry("pages");
        var services = new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton(new AdminContentTypeKeys())
            .BuildServiceProvider();
        var constraint = new AdminContentTypeConstraint();

        bool Match(string value) => constraint.Match(new DefaultHttpContext { RequestServices = services }, null, "contentType",
            new RouteValueDictionary { ["contentType"] = value }, RouteDirection.IncomingRequest);

        Assert.Multiple(() =>
        {
            Assert.That(Match("pages"), Is.True);
            Assert.That(Match("users"), Is.False);
        });
        registry.DidNotReceiveWithAnyArgs().GetHandler(default!);
    }
}
