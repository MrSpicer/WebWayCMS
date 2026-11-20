using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Mcp.Tests;

/// <summary><c>list_content_types</c> with the content type registry: every registered type, with the admin-given names.</summary>
[TestFixture]
public class ContentToolsetContentTypeTests
{
    private sealed record GalleryDTO : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
    }

    private static IAdminCrudHandler Handler(string contentType, string displayName, bool isContentType = true)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.DisplayName.Returns(displayName);
        handler.IsContentType.Returns(isContentType);
        return handler;
    }

    [Test]
    public void ListContentTypes_MergesHandlersAndStoreOnlyTypes_WithRegistrationNames()
    {
        var pages = Handler("pages", "Page");
        var routes = Handler("cmsroutes", "CMS Route", isContentType: false);
        var registry = Substitute.For<IAdminHandlerRegistry>();
        var contentTypes = new ContentTypeRegistry([
            new ContentTypeDescriptor("pages", typeof(PageDTO)),
            new ContentTypeDescriptor("galleries", typeof(GalleryDTO)),
            new ContentTypeDescriptor("albums", typeof(GalleryDTO), "Album"),
        ]);
        var registrations = Substitute.For<IContentTypeRegistrationService>();
        registrations.GetPublishedByKey().Returns(new Dictionary<string, ContentTypeRegistrationDTO>(StringComparer.OrdinalIgnoreCase)
        {
            ["pages"] = new()
            {
                ContentTypeKey = "pages", DisplayName = "Web Page", PluralDisplayName = "Web Pages",
                Description = "Routable pages", Category = "Content",
            },
        });

        var tools = new ContentToolset(registry, [pages, routes], TestCaller.Admin, null, contentTypes, registrations);
        var listed = tools.ListContentTypes().ToDictionary(i => i.ContentType);

        Assert.Multiple(() =>
        {
            Assert.That(listed.Keys, Is.EqualTo(new[] { "pages", "cmsroutes", "galleries", "albums" }));

            Assert.That(listed["pages"].DisplayName, Is.EqualTo("Web Page"));
            Assert.That(listed["pages"].PluralDisplayName, Is.EqualTo("Web Pages"));
            Assert.That(listed["pages"].Description, Is.EqualTo("Routable pages"));
            Assert.That(listed["pages"].Category, Is.EqualTo("Content"));
            Assert.That(listed["pages"].HasAdminHandler, Is.True);
            Assert.That(listed["pages"].IsContentType, Is.True);

            Assert.That(listed["cmsroutes"].DisplayName, Is.EqualTo("CMS Route"));
            Assert.That(listed["cmsroutes"].IsContentType, Is.False);
            Assert.That(listed["cmsroutes"].Description, Is.Null);

            Assert.That(listed["galleries"].DisplayName, Is.EqualTo("Gallery"));
            Assert.That(listed["galleries"].HasAdminHandler, Is.False);
            Assert.That(listed["galleries"].IsContentType, Is.True);
            Assert.That(listed["galleries"].SupportsPublishing, Is.False);
            Assert.That(listed["galleries"].SupportsVersionHistory, Is.False);
            Assert.That(listed["galleries"].HasRegistry, Is.False);
            Assert.That(listed["galleries"].ChildTypes, Is.Empty);
            Assert.That(listed["galleries"].SecondaryApiListKeys, Is.Empty);

            Assert.That(listed["albums"].DisplayName, Is.EqualTo("Album"));
        });
    }
}
