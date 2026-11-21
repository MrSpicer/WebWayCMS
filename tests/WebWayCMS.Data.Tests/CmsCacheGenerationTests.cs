using NUnit.Framework;

using WebWayCMS.Data.Services.Caching;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class CmsCacheGenerationTests
{
    [Test]
    public void Current_StartsAtZero()
    {
        Assert.That(new CmsCacheGeneration().Current, Is.Zero);
    }

    [Test]
    public void Advance_ToHigherValue_MovesCurrentAndRaisesOnce()
    {
        var generation = new CmsCacheGeneration();
        var raised = new List<long>();
        generation.Changed += raised.Add;

        generation.Advance(7);

        Assert.Multiple(() =>
        {
            Assert.That(generation.Current, Is.EqualTo(7));
            Assert.That(raised, Is.EquivalentTo(new[] { 7L }));
        });
    }

    [Test]
    public void Advance_ToLowerOrEqualValue_DoesNothing()
    {
        var generation = new CmsCacheGeneration();
        var raised = new List<long>();
        generation.Changed += raised.Add;
        generation.Advance(5);

        generation.Advance(4);
        generation.Advance(5);

        Assert.Multiple(() =>
        {
            Assert.That(generation.Current, Is.EqualTo(5));
            Assert.That(raised, Is.EquivalentTo(new[] { 5L }));
        });
    }

    [Test]
    public void Advance_SequentialValues_RaisesOncePerValue()
    {
        var generation = new CmsCacheGeneration();
        var raised = new List<long>();
        generation.Changed += raised.Add;

        generation.Advance(1);
        generation.Advance(2);
        generation.Advance(3);

        Assert.That(raised, Is.EquivalentTo(new[] { 1L, 2L, 3L }));
    }

    [Test]
    public void Advance_RacingSameValue_RaisesOnce()
    {
        var generation = new CmsCacheGeneration();
        int raised = 0;
        generation.Changed += _ => Interlocked.Increment(ref raised);

        Parallel.For(0, 64, _ => generation.Advance(9));

        Assert.Multiple(() =>
        {
            Assert.That(generation.Current, Is.EqualTo(9));
            Assert.That(raised, Is.EqualTo(1));
        });
    }

    [Test]
    public void Advance_CommitLosesTheRace_RetriesAndStillRaisesOnce()
    {
        // The CAS lost to a concurrent mover: the loop retries instead of giving up.
        var generation = new LosesOnceGeneration();
        var raised = new List<long>();
        generation.Changed += raised.Add;

        generation.Advance(5);

        Assert.Multiple(() =>
        {
            Assert.That(generation.Current, Is.EqualTo(5));
            Assert.That(raised, Is.EquivalentTo(new[] { 5L }));
        });
    }

    [Test]
    public void AdvanceLocal_MovesKeyNotCurrent_AndNextDbValueIsStillSeen()
    {
        // A failed bump evicts this host through its local epoch without claiming a stamp value the
        // database never issued: another host's real bump to the next value must still be raised.
        var generation = new CmsCacheGeneration();
        var raised = new List<long>();
        generation.Changed += raised.Add;
        generation.Advance(3);

        generation.AdvanceLocal();
        var afterLocal = (generation.Current, generation.Key);
        generation.Advance(4); // poller reads another host's bump

        Assert.Multiple(() =>
        {
            Assert.That(afterLocal, Is.EqualTo((3L, "3.1")));
            Assert.That(generation.Current, Is.EqualTo(4));
            Assert.That(generation.Key, Is.EqualTo("4.1"));
            Assert.That(raised, Is.EqualTo(new[] { 3L, 3L, 4L }));
        });
    }

    [Test]
    public void AdvanceLocal_NoSubscribers_MovesKey()
    {
        var generation = new CmsCacheGeneration();

        generation.AdvanceLocal();

        Assert.That(generation.Key, Is.EqualTo("0.1"));
    }

    [Test]
    public void Key_StartsAtZeroZero()
    {
        Assert.That(new CmsCacheGeneration().Key, Is.EqualTo("0.0"));
    }

    /// <summary>Fails the first commit attempt so the retry loop runs deterministically.</summary>
    private sealed class LosesOnceGeneration : CmsCacheGeneration
    {
        private bool _lostOnce;

        protected override bool TryMoveTo(long expected, long next)
        {
            if (!_lostOnce)
            {
                _lostOnce = true;
                return false;
            }
            return base.TryMoveTo(expected, next);
        }
    }
}
