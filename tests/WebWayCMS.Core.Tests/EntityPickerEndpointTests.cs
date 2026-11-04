using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class EntityPickerEndpointTests
{
    private static IAdminCrudHandler Handler(string contentType, params string[] secondaryKeys)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.HasSecondaryApiList.Returns(secondaryKeys.Length > 0);
        handler.SecondaryApiListKeys.Returns(secondaryKeys);
        return handler;
    }

    private static AdminHandlerRegistry Registry(params IAdminCrudHandler[] handlers) => new(handlers);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_BlankEntityType_ReturnsNull(string? entityType)
    {
        Assert.That(EntityPickerEndpoint.Resolve(entityType, Registry(Handler("faqs"))), Is.Null);
    }

    [Test]
    public void Resolve_NoRegistry_ReturnsNull()
    {
        Assert.That(EntityPickerEndpoint.Resolve("faqs", null), Is.Null);
    }

    [Test]
    public void Resolve_ExactContentType_ReturnsListEndpoint()
    {
        Assert.That(EntityPickerEndpoint.Resolve("faqs", Registry(Handler("faqs"))),
            Is.EqualTo("/wadmin/faqs/api/list"));
    }

    [Test]
    public void Resolve_PluralContentType_ReturnsListEndpointUsingHandlerKey()
    {
        Assert.That(EntityPickerEndpoint.Resolve("ContentBlock", Registry(Handler("contentblocks"))),
            Is.EqualTo("/wadmin/contentblocks/api/list"));
    }

    [Test]
    public void Resolve_SecondaryListKey_ReturnsSecondaryEndpoint()
    {
        var registry = Registry(Handler("pages"), Handler("articles", "other", "articlelists"));

        Assert.That(EntityPickerEndpoint.Resolve("ArticleList", registry),
            Is.EqualTo("/wadmin/articles/api/articlelists"));
    }

    [Test]
    public void Resolve_SecondaryListKeyExactMatch_ReturnsSecondaryEndpoint()
    {
        Assert.That(EntityPickerEndpoint.Resolve("featured", Registry(Handler("articles", "featured"))),
            Is.EqualTo("/wadmin/articles/api/featured"));
    }

    [Test]
    public void Resolve_SecondaryKeysWithoutFlag_AreIgnored()
    {
        var handler = Handler("articles", "articlelists");
        handler.HasSecondaryApiList.Returns(false);

        Assert.That(EntityPickerEndpoint.Resolve("ArticleList", Registry(handler)), Is.Null);
    }

    [Test]
    public void Resolve_UnknownType_ReturnsNull()
    {
        Assert.That(EntityPickerEndpoint.Resolve("Widget", Registry(Handler("articles", "articlelists"))), Is.Null);
    }

    [Test]
    public void Resolve_EscapesPathSegments()
    {
        Assert.That(EntityPickerEndpoint.Resolve("a b", Registry(Handler("a b"))),
            Is.EqualTo("/wadmin/a%20b/api/list"));
    }
}
