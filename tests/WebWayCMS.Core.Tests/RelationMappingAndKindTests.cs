using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Mapping;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class RelationMappingAndKindTests
{
    [Test]
    public void RelationKindMap_SharesEveryValueByName()
    {
        Assert.Multiple(() =>
        {
            foreach (var kind in Enum.GetValues<ContentRelationKind>())
            {
                Assert.That(kind.ToData().ToString(), Is.EqualTo(kind.ToString()));
                Assert.That(kind.ToData().ToDeclaration(), Is.EqualTo(kind));
            }
        });
    }

    [Test]
    public void Mapper_WritesRelationsFromAViewModel_AndFillsThemBack()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile(new FixtureFaqProfile())).CreateMapper();
        var topic = Guid.NewGuid();
        var related = Guid.NewGuid();

        var dto = mapper.Map<FixtureFaqDTO>(new FixtureFaqUpsertViewModel { Title = "Q", FaqTopicId = topic, RelatedFaqIds = [related] });
        var cleared = mapper.Map<FixtureFaqDTO>(new FixtureFaqUpsertViewModel { Title = "Q" });
        dto.Version.Relations = dto.Version.EffectiveRelations().ToList();
        dto.Version.RelationWrites = new Dictionary<string, IReadOnlyList<RelationWrite>>();
        var back = mapper.Map<FixtureFaqUpsertViewModel>(dto);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.TargetOf(nameof(FixtureFaqUpsertViewModel.FaqTopicId)), Is.EqualTo(topic));
            Assert.That(dto.Version.EffectiveRelations().Single(r => r.Name == "FaqTopicId").Kind, Is.EqualTo(RelationKind.Containment));
            Assert.That(dto.Version.TargetsOf(nameof(FixtureFaqUpsertViewModel.RelatedFaqIds)), Is.EqualTo(new[] { related }));
            Assert.That(cleared.Version.RelationWrites.Keys, Is.EquivalentTo(new[] { "FaqTopicId", "RelatedFaqIds" }), "an empty property still writes (clears) its name");
            Assert.That(cleared.Version.RelationWrites.Values, Is.All.Empty);
            Assert.That(back.FaqTopicId, Is.EqualTo(topic));
            Assert.That(back.RelatedFaqIds, Is.EqualTo(new[] { related }));
        });
    }

    [Test]
    public void Mapper_LeavesTypesWithoutRelationsAlone()
    {
        var mapper = TestSupport.CreateMapper();
        var dto = mapper.Map<ContentBlockDTO>(new Models.ContentBlock.ContentBlockUpsertViewModel { Title = "T", Content = "c" });

        Assert.That(dto.Version.RelationWrites, Is.Empty);
    }
}
