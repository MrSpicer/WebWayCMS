using Microsoft.AspNetCore.Http;

using NUnit.Framework;

namespace WebWayCMS.Routing.Tests;

[TestFixture]
public class PreviewConstantsTests
{
    [Test]
    public void MarkDraftRequest_SetsDraftFlagAndNoStore()
    {
        var context = new DefaultHttpContext();

        PreviewConstants.MarkDraftRequest(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Items[PreviewConstants.DraftRequestItemKey], Is.EqualTo(true));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo(PreviewConstants.DraftCacheControl));
        });
    }

    [Test]
    public void MarkDraftRequest_NullContext_Throws()
    {
        Assert.That(() => PreviewConstants.MarkDraftRequest(null!), Throws.ArgumentNullException);
    }
}
