using NUnit.Framework;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class PropertyDefinitionsJsonTests
{
    [Test]
    public void Build_NullType_Throws()
        => Assert.That(() => PropertyDefinitionsJson.Build(null!), Throws.ArgumentNullException);

    [Test]
    public void Build_RoundTripsThroughDeserialize()
    {
        var json = PropertyDefinitionsJson.Build(typeof(InferenceModel));

        var names = PropertyDefinitionsJson.Deserialize(json).Select(p => p.Name);

        Assert.That(names, Is.EqualTo(FormPropertyBuilder.BuildPropertyInfos(typeof(InferenceModel)).Select(p => p.Name)));
    }

    [TestCase(null)]
    [TestCase("  ")]
    public void TryBuild_NoType_IsEmptyWithoutError(string? typeName)
        => Assert.That(PropertyDefinitionsJson.TryBuild(typeName), Is.EqualTo((PropertyDefinitionsJson.Empty, (string?)null)));

    [Test]
    public void TryBuild_ResolvableType_BuildsItsDefinitions()
    {
        var (json, error) = PropertyDefinitionsJson.TryBuild(typeof(InferenceModel).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(json, Is.EqualTo(PropertyDefinitionsJson.Build(typeof(InferenceModel))));
        });
    }

    [Test]
    public void TryBuild_UnknownType_ReportsIt()
    {
        var (json, error) = PropertyDefinitionsJson.TryBuild("No.Such.Type");

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo(PropertyDefinitionsJson.Empty));
            Assert.That(error, Is.EqualTo("Configuration type 'No.Such.Type' could not be resolved."));
        });
    }

    [Test]
    public void TryBuild_UndescribableType_ReportsTheFailure()
    {
        var (json, error) = PropertyDefinitionsJson.TryBuild(typeof(UndescribableModel).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo(PropertyDefinitionsJson.Empty));
            Assert.That(error, Does.StartWith($"Failed to build properties for type '{typeof(UndescribableModel).FullName}'"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("[]")]
    [TestCase("not json")]
    [TestCase("null")]
    public void Deserialize_NothingReadable_IsEmpty(string? json)
        => Assert.That(PropertyDefinitionsJson.Deserialize(json), Is.Empty);

    [Test]
    public void Deserialize_IsCaseInsensitive()
        => Assert.That(PropertyDefinitionsJson.Deserialize("[{\"name\":\"Title\"}]").Single().Name, Is.EqualTo("Title"));
}
