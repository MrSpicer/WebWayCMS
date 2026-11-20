using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentTypeRegistryTests
{
    [Test]
    public void All_KeepsRegistrationOrder_AndGetIsCaseInsensitive()
    {
        var pages = new ContentTypeDescriptor("pages", typeof(PageDTO));
        var articles = new ContentTypeDescriptor("articles", typeof(ArticleDTO));

        var registry = new ContentTypeRegistry([pages, articles]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.All, Is.EqualTo(new[] { pages, articles }));
            Assert.That(registry.Get("PAGES"), Is.SameAs(pages));
            Assert.That(registry.Get("unknown"), Is.Null);
            Assert.That(registry.Get(null), Is.Null);
        });
    }

    [Test]
    public void Ctor_DuplicateKey_Throws()
    {
        Assert.That(
            () => new ContentTypeRegistry([
                new ContentTypeDescriptor("pages", typeof(PageDTO)),
                new ContentTypeDescriptor("Pages", typeof(ArticleDTO)),
            ]),
            Throws.InvalidOperationException.With.Message.Contains("registered twice"));
    }

    [Test]
    public void Ctor_NullDescriptors_Throws()
        => Assert.That(() => new ContentTypeRegistry(null!), Throws.ArgumentNullException);
}
