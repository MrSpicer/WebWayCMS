using Microsoft.AspNetCore.Http;

using NUnit.Framework;

using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ZoneEditRenderLogTests
{
    [Test]
    public void For_CreatesOncePerRequest()
    {
        var context = new DefaultHttpContext();

        var log = ZoneEditRenderLog.For(context);

        Assert.Multiple(() =>
        {
            Assert.That(ZoneEditRenderLog.For(context), Is.SameAs(log));
            Assert.That(context.Items[ZoneEditRenderLog.ItemsKey], Is.SameAs(log));
            Assert.That(ZoneEditRenderLog.For(new DefaultHttpContext()), Is.Not.SameAs(log));
            Assert.That(() => ZoneEditRenderLog.For(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void NextUnplaced_PrefersRenderedOwners_ThenTheRest_SkippingRenderedAndListed()
    {
        var log = new ZoneEditRenderLog();
        var renderedOwner = Guid.NewGuid();
        var hiddenOwner = Guid.NewGuid();
        var renderedZone = Guid.NewGuid();
        log.ItemRendered(renderedOwner);
        log.ZoneRendered(renderedZone);
        var shown = new UnplacedZoneCandidate(renderedZone, renderedOwner, "shown");
        var stranded = new UnplacedZoneCandidate(Guid.NewGuid(), renderedOwner, "stranded");
        var underHidden = new UnplacedZoneCandidate(Guid.NewGuid(), hiddenOwner, "under hidden");
        var all = new[] { shown, stranded, underHidden };
        var listed = new HashSet<Guid>();

        var first = log.NextUnplaced(all, listed);
        listed.Add(stranded.ZoneNodeId);
        var second = log.NextUnplaced(all, listed);
        listed.Add(underHidden.ZoneNodeId);
        var third = log.NextUnplaced(all, listed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { stranded }));
            Assert.That(second, Is.EqualTo(new[] { underHidden }));
            Assert.That(third, Is.Empty);
            Assert.That(log.WasRendered(renderedZone), Is.True, "the zone the editor rendered is skipped on a re-check");
            Assert.That(log.WasRendered(stranded.ZoneNodeId), Is.False);
            Assert.That(() => log.NextUnplaced(null!, listed), Throws.ArgumentNullException);
            Assert.That(() => log.NextUnplaced(all, null!), Throws.ArgumentNullException);
        });
    }
}
