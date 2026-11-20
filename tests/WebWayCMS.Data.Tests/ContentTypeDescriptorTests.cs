using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentTypeDescriptorTests
{
    private sealed record ArticleListDTO : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
    }

    private sealed record FAQTopicDTO : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
    }

    private sealed record Gallery : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
    }

    private sealed class DTO;

    [Test]
    public void Ctor_KeepsKeyTypeAndNames()
    {
        var descriptor = new ContentTypeDescriptor("articlelists", typeof(ArticleListDTO), "Article List", "Article Lists");

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Key, Is.EqualTo("articlelists"));
            Assert.That(descriptor.ClrType, Is.EqualTo(typeof(ArticleListDTO)));
            Assert.That(descriptor.DisplayName, Is.EqualTo("Article List"));
            Assert.That(descriptor.PluralDisplayName, Is.EqualTo("Article Lists"));
        });
    }

    [Test]
    public void Ctor_BlankNames_AreNull()
    {
        var descriptor = new ContentTypeDescriptor("galleries", typeof(Gallery), " ", "");

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.DisplayName, Is.Null);
            Assert.That(descriptor.PluralDisplayName, Is.Null);
        });
    }

    [Test]
    public void Ctor_InvalidArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentTypeDescriptor(null!, typeof(Gallery)), Throws.ArgumentNullException);
            Assert.That(() => new ContentTypeDescriptor(" ", typeof(Gallery)), Throws.ArgumentException);
            Assert.That(() => new ContentTypeDescriptor("galleries", null!), Throws.ArgumentNullException);
            Assert.That(() => new ContentTypeDescriptor(new string('k', ContentTypeDescriptor.MaxKeyLength + 1), typeof(Gallery)),
                Throws.ArgumentException.With.Message.Contains("longer than"));
            Assert.That(() => new ContentTypeDescriptor("strings", typeof(string)),
                Throws.ArgumentException.With.Message.Contains(nameof(IVersionedContent)));
        });
    }

    [Test]
    public void Ctor_KeyAtMaxLength_IsAccepted()
        => Assert.That(new ContentTypeDescriptor(new string('k', ContentTypeDescriptor.MaxKeyLength), typeof(Gallery)).Key,
            Has.Length.EqualTo(ContentTypeDescriptor.MaxKeyLength));

    [TestCase(typeof(ArticleListDTO), "Article List")]
    [TestCase(typeof(FAQTopicDTO), "FAQ Topic")]
    [TestCase(typeof(Gallery), "Gallery")]
    [TestCase(typeof(DTO), "DTO")]
    public void DeriveDisplayName_SplitsTheTypeNameWithoutDtoSuffix(Type type, string expected)
        => Assert.That(ContentTypeDescriptor.DeriveDisplayName(type), Is.EqualTo(expected));

    [Test]
    public void DeriveDisplayName_NullType_Throws()
        => Assert.That(() => ContentTypeDescriptor.DeriveDisplayName(null!), Throws.ArgumentNullException);

    [TestCase("Page", "Pages")]
    [TestCase("Article List", "Article Lists")]
    [TestCase("FAQ", "FAQs")]
    [TestCase("FAQ Topic", "FAQ Topics")]
    [TestCase("Address", "Addresses")]
    [TestCase("Box", "Boxes")]
    [TestCase("Batch", "Batches")]
    [TestCase("Wish", "Wishes")]
    [TestCase("Category", "Categories")]
    [TestCase("Key", "Keys")]
    [TestCase("y", "ys")]
    [TestCase("", "")]
    [TestCase("  ", "  ")]
    [TestCase(null, "")]
    public void Pluralize_FollowsEnglishRules(string? singular, string expected)
        => Assert.That(ContentTypeDescriptor.Pluralize(singular), Is.EqualTo(expected));
}
