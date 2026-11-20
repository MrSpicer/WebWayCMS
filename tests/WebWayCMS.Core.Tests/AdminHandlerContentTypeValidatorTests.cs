using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class AdminHandlerContentTypeValidatorTests
{
    private static readonly IContentTypeRegistry ContentTypes =
        new ContentTypeRegistry([new ContentTypeDescriptor("pages", typeof(PageDTO))]);

    private static IAdminCrudHandler Handler(string contentType, bool isContentType)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.IsContentType.Returns(isContentType);
        return handler;
    }

    [Test]
    public void Validate_RegisteredContentTypes_AndNonContentHandlers_Pass()
    {
        var handlers = new HandlerRegistryStub([Handler("Pages", true), Handler("cmsroutes", false)]);

        Assert.That(() => AdminHandlerContentTypeValidator.Validate(handlers, ContentTypes), Throws.Nothing);
    }

    [Test]
    public void Validate_ContentHandlerWithoutAStore_Throws()
    {
        var handlers = new HandlerRegistryStub([Handler("pages", true), Handler("pagez", true)]);

        Assert.That(() => AdminHandlerContentTypeValidator.Validate(handlers, ContentTypes),
            Throws.InvalidOperationException.With.Message.Contains("'pagez'"));
    }

    [Test]
    public void Validate_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => AdminHandlerContentTypeValidator.Validate(null!, ContentTypes), Throws.ArgumentNullException);
            Assert.That(() => AdminHandlerContentTypeValidator.Validate(new HandlerRegistryStub([]), null!), Throws.ArgumentNullException);
        });
    }
}
