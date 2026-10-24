using Microsoft.AspNetCore.Http;

using NUnit.Framework;

using WebWayCMS.Data.Services;

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

    [Test]
    public void MarkSnapshotRequest_StoresTheSnapshotAndNoStore()
    {
        var context = new DefaultHttpContext();
        var snapshot = new ContentReadSnapshot([Guid.NewGuid()], [Guid.NewGuid()]);

        PreviewConstants.MarkSnapshotRequest(context, snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(context.Items[PreviewConstants.SnapshotRequestItemKey], Is.SameAs(snapshot));
            Assert.That(context.Items.ContainsKey(PreviewConstants.DraftRequestItemKey), Is.False);
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo(PreviewConstants.DraftCacheControl));
        });
    }

    [Test]
    public void MarkSnapshotRequest_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => PreviewConstants.MarkSnapshotRequest(null!, new ContentReadSnapshot([], [])), Throws.ArgumentNullException);
            Assert.That(() => PreviewConstants.MarkSnapshotRequest(new DefaultHttpContext(), null!), Throws.ArgumentNullException);
        });
    }
}
