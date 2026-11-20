using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Host.Tests;

/// <summary>A host content type joins the content type registry with no extra call.</summary>
[TestFixture]
public class WebWayCmsBuilderContentTypeTests
{
    private sealed record TestDto : IVersionedContent
    {
        public Guid VersionId { get; set; }
        public ContentVersion Version { get; set; } = new();
    }

    private static WebWayCmsBuilder Builder(ServiceCollection services)
        => new(services, new ConfigurationBuilder().Build());

    [Test]
    public void AddContentType_RegistersItsDescriptor_WithTheGivenNames()
    {
        var services = new ServiceCollection();

        Builder(services).AddContentType<TestDto>("testdtos", "Test Item", "Test Items");

        var descriptor = services
            .Where(d => d.ServiceType == typeof(ContentTypeDescriptor))
            .Select(d => (ContentTypeDescriptor)d.ImplementationInstance!)
            .Single();
        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Key, Is.EqualTo("testdtos"));
            Assert.That(descriptor.ClrType, Is.EqualTo(typeof(TestDto)));
            Assert.That(descriptor.DisplayName, Is.EqualTo("Test Item"));
            Assert.That(descriptor.PluralDisplayName, Is.EqualTo("Test Items"));
        });
    }

    [Test]
    public void AddContentType_WithoutNames_LeavesThemToTheSync()
    {
        var services = new ServiceCollection();

        Builder(services).AddContentType<TestDto>("testdtos");

        var descriptor = (ContentTypeDescriptor)services.Single(d => d.ServiceType == typeof(ContentTypeDescriptor)).ImplementationInstance!;
        Assert.That(descriptor.DisplayName, Is.Null);
    }
}
