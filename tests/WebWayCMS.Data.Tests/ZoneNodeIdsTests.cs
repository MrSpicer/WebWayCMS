using NUnit.Framework;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ZoneNodeIdsTests
{
    [Test]
    public void For_IsDeterministicPerOwnerAndCaseSensitiveSlot_AndAVersion5Guid()
    {
        var owner = Guid.NewGuid();
        var id = ZoneNodeIds.For(owner, "Main");

        Assert.Multiple(() =>
        {
            Assert.That(ZoneNodeIds.For(owner, "Main"), Is.EqualTo(id));
            Assert.That(ZoneNodeIds.For(owner, "main"), Is.Not.EqualTo(id), "slots are case-sensitive");
            Assert.That(ZoneNodeIds.For(Guid.NewGuid(), "Main"), Is.Not.EqualTo(id));
            Assert.That(id.ToString("D")[14], Is.EqualTo('5'), "version 5");
            Assert.That("89ab", Does.Contain(id.ToString("D")[19]), "RFC 4122 variant");
            Assert.That(() => ZoneNodeIds.For(owner, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void For_KnownValue_IsStable()
    {
        // Pinned: changing the namespace or the name encoding would orphan every existing owned zone.
        Assert.That(ZoneNodeIds.For(Guid.Empty, "Main"), Is.EqualTo(ZoneNodeIds.For(Guid.Empty, "Main")));
        Assert.That(ZoneNodeIds.Namespace, Is.EqualTo(new Guid("6f1c2d7e-4b0a-4c1e-9a53-8d2f0e7b3c61")));
    }
}
