using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Data.Services;
using WebWayCMS.Startup;

namespace WebWayCMS.Host.Tests;

[TestFixture]
public class ContentReadContextTests
{
    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "TestAuth"));

    private static IHttpContextAccessor Accessor(HttpContext? context) =>
        new HttpContextAccessor { HttpContext = context };

    private static HttpContext Context(ClaimsPrincipal? user = null, string? previewCookie = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = user ?? new ClaimsPrincipal(new ClaimsIdentity());
        ctx.Request.Cookies = previewCookie == null
            ? Substitute.For<IRequestCookieCollection>()
            : Cookies(new Dictionary<string, string> { ["wwcms_preview"] = previewCookie });
        return ctx;
    }

    private static IRequestCookieCollection Cookies(IReadOnlyDictionary<string, string> pairs)
    {
        var cookies = Substitute.For<IRequestCookieCollection>();
        foreach (var pair in pairs)
        {
            string? value;
            cookies.TryGetValue(pair.Key, out value).Returns(callInfo =>
            {
                callInfo[1] = pair.Value;
                return true;
            });
        }

        return cookies;
    }

    [Test]
    public void PublishedContentReadContext_IsAlwaysPublished()
    {
        var context = new PublishedContentReadContext();

        Assert.Multiple(() =>
        {
            Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
            Assert.That(context.Culture, Is.Empty);
            Assert.That(context.Segment, Is.Empty);
        });
    }

    [Test]
    public void PreviewAwareReadContext_NullAccessor_Throws()
    {
        Assert.That(() => new PreviewAwareReadContext(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void PreviewAwareReadContext_CultureAndSegment_AreEmpty()
    {
        var context = new PreviewAwareReadContext(Accessor(Context()));

        Assert.Multiple(() =>
        {
            Assert.That(context.Culture, Is.Empty);
            Assert.That(context.Segment, Is.Empty);
        });
    }

    [Test]
    public void PreviewAwareReadContext_NoHttpContext_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(null));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_Unauthenticated_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context()));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_NullUser_ReturnsPublished()
    {
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal)null!);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var context = new PreviewAwareReadContext(accessor);

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_NullIdentity_ReturnsPublished()
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal() };
        var context = new PreviewAwareReadContext(Accessor(ctx));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AuthenticatedNonEditor_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Guest"))));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithoutCookie_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Admin"))));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithDraftRequestFlag_ReturnsDraft()
    {
        var ctx = Context(Principal("Admin"));
        ctx.Items[PreviewConstants.DraftRequestItemKey] = true;

        Assert.That(new PreviewAwareReadContext(Accessor(ctx)).Mode, Is.EqualTo(ContentReadMode.Draft));
    }

    [Test]
    public void PreviewAwareReadContext_DraftRequestFlagNotTrue_ReturnsPublished()
    {
        var ctx = Context(Principal("Admin"));
        ctx.Items[PreviewConstants.DraftRequestItemKey] = "true";

        Assert.That(new PreviewAwareReadContext(Accessor(ctx)).Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_NonEditorWithDraftRequestFlag_ReturnsPublished()
    {
        var ctx = Context(Principal("Guest"));
        ctx.Items[PreviewConstants.DraftRequestItemKey] = true;

        Assert.That(new PreviewAwareReadContext(Accessor(ctx)).Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_EditorWithDraftRequestFlag_ReturnsDraft()
    {
        var ctx = Context(Principal("Editor"));
        ctx.Items[PreviewConstants.DraftRequestItemKey] = true;

        Assert.That(new PreviewAwareReadContext(Accessor(ctx)).Mode, Is.EqualTo(ContentReadMode.Draft));
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithSnapshot_ReadsTheSnapshot_EvenWhenAlsoMarkedDraft()
    {
        var ctx = Context(Principal("Admin"));
        var snapshot = new ContentReadSnapshot([Guid.NewGuid()], [Guid.NewGuid()]);
        ctx.Items[PreviewConstants.SnapshotRequestItemKey] = snapshot;
        ctx.Items[PreviewConstants.DraftRequestItemKey] = true;

        var context = new PreviewAwareReadContext(Accessor(ctx));

        Assert.Multiple(() =>
        {
            Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Snapshot));
            Assert.That(context.Snapshot, Is.SameAs(snapshot));
        });
    }

    [Test]
    public void PreviewAwareReadContext_EditorWithSnapshot_DoesNotReadIt()
    {
        var ctx = Context(Principal("Editor"));
        ctx.Items[PreviewConstants.SnapshotRequestItemKey] = new ContentReadSnapshot([], []);

        var context = new PreviewAwareReadContext(Accessor(ctx));

        Assert.Multiple(() =>
        {
            Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
            Assert.That(context.Snapshot, Is.Null);
        });
    }

    [Test]
    public void PreviewAwareReadContext_SnapshotItemOfTheWrongType_IsIgnored()
    {
        var ctx = Context(Principal("Admin"));
        ctx.Items[PreviewConstants.SnapshotRequestItemKey] = "not a snapshot";

        Assert.That(new PreviewAwareReadContext(Accessor(ctx)).Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PublishedContentReadContext_NeverReadsASnapshot()
    {
        IContentReadContext context = new PublishedContentReadContext();

        Assert.That(context.Snapshot, Is.Null);
    }

    [Test]
    public void PreviewAwareReadContext_AdminWithLegacyPreviewCookie_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Admin"), previewCookie: "1")));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void PreviewAwareReadContext_EditorWithLegacyPreviewCookie_ReturnsPublished()
    {
        var context = new PreviewAwareReadContext(Accessor(Context(Principal("Editor"), previewCookie: "1")));

        Assert.That(context.Mode, Is.EqualTo(ContentReadMode.Published));
    }

    [Test]
    public void HttpContentUserContext_NullAccessor_Throws()
    {
        Assert.That(() => new HttpContentUserContext(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void HttpContentUserContext_NoHttpContext_ReturnsNull()
    {
        var context = new HttpContentUserContext(Accessor(null));

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_NoNameIdentifierClaim_ReturnsNull()
    {
        var context = new HttpContentUserContext(Accessor(Context(Principal("Admin"))));

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_NullUser_ReturnsNull()
    {
        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns((ClaimsPrincipal)null!);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var context = new HttpContentUserContext(accessor);

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_InvalidGuidClaim_ReturnsNull()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-guid") }, "TestAuth"));
        var context = new HttpContentUserContext(Accessor(Context(user)));

        Assert.That(context.CurrentUserId, Is.Null);
    }

    [Test]
    public void HttpContentUserContext_ValidGuidClaim_ReturnsUserId()
    {
        var expected = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, expected.ToString()) }, "TestAuth"));
        var context = new HttpContentUserContext(Accessor(Context(user)));

        Assert.That(context.CurrentUserId, Is.EqualTo(expected));
    }
}
