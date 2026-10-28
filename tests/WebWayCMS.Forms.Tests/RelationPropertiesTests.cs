using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class RelationPropertiesTests
{
    private sealed class Shapes
    {
        [ContentRelation("lists", Kind = ContentRelationKind.Containment, Required = true)]
        public Guid Single { get; set; }

        [ContentRelation("pages")]
        public Guid? Optional { get; set; }

        [ContentRelation("faqs")]
        public List<Guid> AsList { get; set; } = [];

        [ContentRelation("faqs")]
        public Guid[] AsArray { get; set; } = [];

        [ContentRelation("faqs")]
        public IEnumerable<Guid> AsEnumerable { get; init; } = [];

        public Guid NotARelation { get; set; }
    }

    private sealed class BadShape
    {
        [ContentRelation("x")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ReadOnlyRelation
    {
        [ContentRelation("x")]
        public Guid Id => Guid.Empty;
    }

    private sealed class WriteOnlyRelation
    {
        [ContentRelation("x")]
        public Guid Id { set { } }
    }

    private sealed class HashSetShape
    {
        [ContentRelation("x")]
        public HashSet<Guid> Ids { get; set; } = [];
    }

    [Test]
    public void ContentRelationAttribute_Defaults_AndTracksAnExplicitDeletePolicy()
    {
        var plain = new ContentRelationAttribute("faqs");
        var detach = new ContentRelationAttribute("faqs") { OnTargetDelete = RelationDeletePolicy.Detach };

        Assert.Multiple(() =>
        {
            Assert.That(plain.TargetContentType, Is.EqualTo("faqs"));
            Assert.That(plain.Kind, Is.EqualTo(ContentRelationKind.Reference));
            Assert.That(plain.Required, Is.False);
            Assert.That(plain.Ordered, Is.False);
            Assert.That(plain.OnTargetDelete, Is.EqualTo(RelationDeletePolicy.Restrict));
            Assert.That(plain.IsOnTargetDeleteSet, Is.False);
            Assert.That(detach.OnTargetDelete, Is.EqualTo(RelationDeletePolicy.Detach));
            Assert.That(detach.IsOnTargetDeleteSet, Is.True);
            Assert.That(() => new ContentRelationAttribute(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Describe_FindsEveryShape_AndIsCached()
    {
        var relations = RelationProperties.Describe(typeof(Shapes));

        Assert.Multiple(() =>
        {
            Assert.That(relations.Select(r => (r.Name, r.IsMany, r.IsOptional)), Is.EqualTo(new[]
            {
                ("Single", false, false),
                ("Optional", false, true),
                ("AsList", true, true),
                ("AsArray", true, true),
                ("AsEnumerable", true, true)
            }));
            Assert.That(relations[0].Attribute.Kind, Is.EqualTo(ContentRelationKind.Containment));
            Assert.That(RelationProperties.Describe(typeof(Shapes)), Is.SameAs(relations));
            Assert.That(() => RelationProperties.Describe(null!), Throws.ArgumentNullException);
        });
    }

    [TestCase(typeof(BadShape), "must be a Guid")]
    [TestCase(typeof(HashSetShape), "must be a Guid")]
    [TestCase(typeof(ReadOnlyRelation), "public getter and setter")]
    [TestCase(typeof(WriteOnlyRelation), "public getter and setter")]
    public void Describe_UnsupportedProperty_Throws(Type type, string message)
        => Assert.That(() => RelationProperties.Describe(type), Throws.InvalidOperationException.With.Message.Contains(message));

    [Test]
    public void Read_SkipsEmptyAndDuplicateIds_AndNumbersManyInOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var model = new Shapes
        {
            Single = a,
            Optional = null,
            AsList = [b, Guid.Empty, a, b],
            AsArray = [],
            AsEnumerable = null!
        };

        var values = RelationProperties.Read(model);

        Assert.Multiple(() =>
        {
            Assert.That(values, Is.EqualTo(new[]
            {
                new RelationValue("Single", a, 0, ""),
                new RelationValue("AsList", b, 0, ""),
                new RelationValue("AsList", a, 1, "")
            }));
            Assert.That(RelationProperties.Read(new Shapes { Optional = Guid.Empty }), Is.Empty);
            Assert.That(() => RelationProperties.Read(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Write_SetsEveryShape_ByOrdinal_AndClearsWhatIsMissing()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var model = new Shapes { Single = Guid.NewGuid(), Optional = Guid.NewGuid(), AsList = [a] };

        RelationProperties.Write(model,
        [
            new RelationValue("AsArray", b, 1, ""),
            new RelationValue("AsArray", a, 0, ""),
            new RelationValue("AsEnumerable", a, 0, ""),
            new RelationValue("Optional", b, 0, "")
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(model.Single, Is.EqualTo(Guid.Empty));
            Assert.That(model.Optional, Is.EqualTo(b));
            Assert.That(model.AsList, Is.Empty);
            Assert.That(model.AsArray, Is.EqualTo(new[] { a, b }));
            Assert.That(model.AsEnumerable, Is.EqualTo(new[] { a }));
        });

        RelationProperties.Write(model, []);
        Assert.Multiple(() =>
        {
            Assert.That(model.Optional, Is.Null);
            Assert.That(() => RelationProperties.Write(null!, []), Throws.ArgumentNullException);
            Assert.That(() => RelationProperties.Write(model, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void FormPropertyBuilder_ARelationImpliesAnEntityPicker()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(Shapes)).ToDictionary(i => i.Name);

        Assert.Multiple(() =>
        {
            var single = infos["Single"];
            Assert.That(single.FormComponent, Is.EqualTo("EntityPicker"));
            Assert.That(single.EntityType, Is.EqualTo("lists"));
            Assert.That(single.EditorType, Is.EqualTo(EditorType.Guid));
            Assert.That(single.IsRequired, Is.True);
            Assert.That(single.IsMultiple, Is.False);
            Assert.That(single.RelationKind, Is.EqualTo(ContentRelationKind.Containment));
            Assert.That(infos["Optional"].IsMultiple, Is.False);
            Assert.That(infos["AsList"].IsMultiple, Is.True);
            Assert.That(infos["AsList"].RelationKind, Is.EqualTo(ContentRelationKind.Reference));
            Assert.That(infos["NotARelation"].RelationKind, Is.Null);
            Assert.That(infos["NotARelation"].FormComponent, Is.Empty);
        });
    }

    private sealed class Overridden
    {
        [ContentRelation("lists")]
        [FormProperty(Label = "List", FormComponent = "Custom", EntityType = "Other", EditorType = EditorType.Hidden, IsRequired = true)]
        public Guid Explicit { get; set; }

        [ContentRelation("lists")]
        [FormProperty(Label = "Defaulted")]
        public Guid Defaulted { get; set; }
    }

    [Test]
    public void FormPropertyBuilder_ExplicitFormPropertySettingsWin()
    {
        var infos = FormPropertyBuilder.BuildPropertyInfos(typeof(Overridden)).ToDictionary(i => i.Name);

        Assert.Multiple(() =>
        {
            Assert.That(infos["Explicit"].FormComponent, Is.EqualTo("Custom"));
            Assert.That(infos["Explicit"].EntityType, Is.EqualTo("Other"));
            Assert.That(infos["Explicit"].EditorType, Is.EqualTo(EditorType.Hidden));
            Assert.That(infos["Explicit"].IsRequired, Is.True, "Required only ever adds");
            Assert.That(infos["Defaulted"].FormComponent, Is.EqualTo("EntityPicker"));
            Assert.That(infos["Defaulted"].EntityType, Is.EqualTo("lists"));
            Assert.That(infos["Defaulted"].EditorType, Is.EqualTo(EditorType.Guid));
            Assert.That(infos["Defaulted"].Label, Is.EqualTo("Defaulted"));
        });
    }
}
