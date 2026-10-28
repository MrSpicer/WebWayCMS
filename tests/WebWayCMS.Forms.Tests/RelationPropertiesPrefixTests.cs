using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class RelationPropertiesPrefixTests
{
    private sealed class ConfigModel
    {
        [ContentRelation("images")]
        public Guid? ImageId { get; set; }

        [ContentRelation("articles")]
        public Guid RequiredArticleId { get; set; }

        [ContentRelation("faqs")]
        public List<Guid> FaqIds { get; set; } = [];
    }

    [Test]
    public void Read_WithPrefix_PrefixesEveryName()
    {
        var image = Guid.NewGuid();
        var article = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var model = new ConfigModel
        {
            ImageId = image,
            RequiredArticleId = article,
            FaqIds = [first, second]
        };

        var values = RelationProperties.Read(model, RelationProperties.ConfigPrefix);

        Assert.Multiple(() =>
        {
            Assert.That(values.Select(v => v.Name), Is.EquivalentTo(new[]
            {
                "config.ImageId", "config.RequiredArticleId", "config.FaqIds", "config.FaqIds"
            }));
            Assert.That(values.Single(v => v.Name == "config.ImageId").TargetNodeId, Is.EqualTo(image));
            Assert.That(values.Where(v => v.Name == "config.FaqIds").OrderBy(v => v.Ordinal).Select(v => v.TargetNodeId),
                Is.EqualTo(new[] { first, second }));
        });
    }

    [Test]
    public void Write_WithPrefix_MatchesPrefixedNamesAndClearsTheRest()
    {
        var model = new ConfigModel();
        var image = Guid.NewGuid();
        var many = new[] { Guid.NewGuid(), Guid.NewGuid() };

        RelationProperties.Write(model,
        [
            new RelationValue("config.ImageId", image, 0, string.Empty),
            new RelationValue("config.FaqIds", many[1], 1, string.Empty),
            new RelationValue("config.FaqIds", many[0], 0, string.Empty)
        ], RelationProperties.ConfigPrefix);

        Assert.Multiple(() =>
        {
            Assert.That(model.ImageId, Is.EqualTo(image));
            Assert.That(model.RequiredArticleId, Is.EqualTo(Guid.Empty));
            Assert.That(model.FaqIds, Is.EqualTo(many));
        });
    }

    [Test]
    public void Write_UnprefixedValuesWithPrefixArgument_AreIgnored()
    {
        var model = new ConfigModel { ImageId = Guid.NewGuid() };

        RelationProperties.Write(model, [new RelationValue("ImageId", Guid.NewGuid(), 0, string.Empty)],
            RelationProperties.ConfigPrefix);

        Assert.That(model.ImageId, Is.Null);
    }

    [Test]
    public void FormFieldContext_IsMultiple_DelegatesToTheProperty()
    {
        var many = new FormFieldContext { Property = new FormPropertyInfo { IsMultiple = true } };
        var single = new FormFieldContext { Property = new FormPropertyInfo { IsMultiple = false } };

        Assert.Multiple(() =>
        {
            Assert.That(many.IsMultiple, Is.True);
            Assert.That(single.IsMultiple, Is.False);
        });
    }

    [Test]
    public void GetSettingsAndNullArguments_BehaveAsDocumented()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RelationProperties.ConfigPrefix, Is.EqualTo("config."));
            Assert.That(() => RelationProperties.Read(null!, RelationProperties.ConfigPrefix), Throws.ArgumentNullException);
            Assert.That(() => RelationProperties.Write(new ConfigModel(), null!, RelationProperties.ConfigPrefix),
                Throws.ArgumentNullException);
        });
    }
}
