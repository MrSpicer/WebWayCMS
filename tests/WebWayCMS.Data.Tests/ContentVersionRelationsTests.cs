using NUnit.Framework;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentVersionRelationsTests
{
    private static ContentRelation Row(string name, Guid target, int ordinal = 0, string qualifier = "", RelationKind kind = RelationKind.Reference)
        => new() { Id = Guid.NewGuid(), Name = name, TargetNodeId = target, Ordinal = ordinal, Qualifier = qualifier, Kind = kind };

    [Test]
    public void TargetOf_And_TargetsOf_ReadTheStoredRowsByOrdinal()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var version = new ContentVersion { Relations = [Row("Many", b, 1), Row("Many", a, 0), Row("One", a)] };

        Assert.Multiple(() =>
        {
            Assert.That(version.TargetOf("One"), Is.EqualTo(a));
            Assert.That(version.TargetOf("Missing"), Is.Null);
            Assert.That(version.TargetsOf("Many"), Is.EqualTo(new[] { a, b }));
            Assert.That(version.TargetsOf("Missing"), Is.Empty);
        });
    }

    [Test]
    public void SetRelation_WritesOneTargetOrNone_OverTheStoredRows()
    {
        var stored = Guid.NewGuid();
        var target = Guid.NewGuid();
        var version = new ContentVersion { Relations = [Row("One", stored), Row("Other", stored)] };

        version.SetRelation("One", target, RelationKind.Containment, "slot");
        var written = version.EffectiveRelations().Single(r => r.Name == "One");
        version.SetRelation("Other", Guid.Empty, RelationKind.Reference);
        var afterEmpty = version.TargetOf("Other");
        version.SetRelation("Other", null, RelationKind.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(written.TargetNodeId, Is.EqualTo(target));
            Assert.That(written.Kind, Is.EqualTo(RelationKind.Containment));
            Assert.That(written.Qualifier, Is.EqualTo("slot"));
            Assert.That(afterEmpty, Is.Null, "an empty id clears the relation");
            Assert.That(version.TargetOf("Other"), Is.Null);
            Assert.That(version.Relations, Has.Count.EqualTo(2), "the stored rows are untouched");
        });
    }

    [Test]
    public void SetRelations_SkipsEmptyIds_AndNumbersTheRest()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var version = new ContentVersion();

        version.SetRelations("Many", [a, Guid.Empty, b], RelationKind.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(version.TargetsOf("Many"), Is.EqualTo(new[] { a, b }));
            Assert.That(version.RelationWrites["Many"].Select(w => w.Ordinal), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void Writes_AreCopyOnWrite_SoAWithCopyNeverSharesThem()
    {
        var original = new ContentVersion();
        var copy = original with { };

        copy.SetRelation("One", Guid.NewGuid(), RelationKind.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(original.RelationWrites, Is.Empty);
            Assert.That(copy.RelationWrites, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ReplaceAllRelationsFrom_CopiesTheSourceAndClearsNamesItLacks()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var version = new ContentVersion { Relations = [Row("Kept", mine), Row("Dropped", mine)] };
        version.SetRelation("Pending", mine, RelationKind.Reference);

        version.ReplaceAllRelationsFrom([Row("Kept", theirs, 2, "q", RelationKind.Owned)]);

        var kept = version.EffectiveRelations().Single();
        Assert.Multiple(() =>
        {
            Assert.That(kept.Name, Is.EqualTo("Kept"));
            Assert.That(kept.TargetNodeId, Is.EqualTo(theirs));
            Assert.That(kept.Ordinal, Is.EqualTo(2));
            Assert.That(kept.Qualifier, Is.EqualTo("q"));
            Assert.That(kept.Kind, Is.EqualTo(RelationKind.Owned));
            Assert.That(version.TargetOf("Dropped"), Is.Null);
            Assert.That(version.TargetOf("Pending"), Is.Null);
        });
    }

    [Test]
    public void Overlay_KeepsUnwrittenBaselineNames_UnderTheNewVersion()
    {
        var target = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var baseline = Row("Kept", target);
        var writes = new Dictionary<string, IReadOnlyList<RelationWrite>> { ["New"] = [new RelationWrite(target, 0, "", RelationKind.Reference)] };

        var rows = ContentVersionRelations.Overlay([baseline], writes, owner);

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.Name), Is.EquivalentTo(new[] { "Kept", "New" }));
            Assert.That(rows.Select(r => r.SourceVersionId), Is.All.EqualTo(owner));
            Assert.That(rows.Single(r => r.Name == "Kept").Id, Is.Not.EqualTo(baseline.Id), "rows are copied, never re-parented");
        });
    }

    [Test]
    public void RelationFingerprint_IsOrderIndependent_AndEmptyWithoutRelations()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = new ContentVersion { Relations = [Row("X", a), Row("Y", b)] };
        var second = new ContentVersion { Relations = [Row("Y", b), Row("X", a)] };

        Assert.Multiple(() =>
        {
            Assert.That(first.RelationFingerprint(), Is.EqualTo(second.RelationFingerprint()));
            Assert.That(new ContentVersion().RelationFingerprint(), Is.Empty);
        });
    }
}
