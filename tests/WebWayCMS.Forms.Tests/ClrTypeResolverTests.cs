using NUnit.Framework;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class ClrTypeResolverTests
{
    [Test]
    public void Resolve_FrameworkType_ResolvesDirectly()
        => Assert.That(ClrTypeResolver.Resolve("System.String"), Is.EqualTo(typeof(string)));

    [Test]
    public void Resolve_TypeInAnotherLoadedAssembly_ResolvesByFullName()
        => Assert.That(ClrTypeResolver.Resolve(typeof(ClrTypeResolverTests).FullName), Is.EqualTo(typeof(ClrTypeResolverTests)));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_Empty_ReturnsNull(string? typeName)
        => Assert.That(ClrTypeResolver.Resolve(typeName), Is.Null);

    [TestCase("This.Type.Does.Not.Exist")]
    [TestCase("SomeType, NonExistentAssembly.DoesNotExist.Version99")]
    public void Resolve_Unknown_ReturnsNull(string typeName)
        => Assert.That(ClrTypeResolver.Resolve(typeName), Is.Null);

    [TestCase("SomeType, SomeAssembly, Version=not.a.version")]
    [TestCase("[")]
    [TestCase("a,b,c,d")]
    [TestCase("System.TypedReference[]")]
    public void Resolve_Malformed_ReturnsNull(string typeName)
        => Assert.That(ClrTypeResolver.Resolve(typeName), Is.Null);
}
