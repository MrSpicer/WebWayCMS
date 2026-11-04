using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

using WebWayCMS.Content;
using WebWayCMS.Services.ContentSeeding;

namespace WebWayCMS.Host.Tests;

/// <summary>
/// The CMS ships its own pages (Home, Dashboard, Users, Roles) as an embedded seed file in the admin assembly,
/// imported by the ordinary JSON content seeder.
/// </summary>
[TestFixture]
public class CmsPagesContentSeedTests
{
    private const string ResourceSuffix = "cms-pages.contentseed.json";

    private static ContentSeedSource LoadCmsPagesSource()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=u;Password=p"
        });
        builder.Services.AddWebWayCmsAdmin(builder.Configuration);
        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        return scope.ServiceProvider.GetServices<IContentSeedSourceProvider>()
            .SelectMany(p => p.GetSources())
            .Single(s => s.Name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
    }

    [Test]
    public void AdminHost_DiscoversTheEmbeddedCmsPagesSeed()
    {
        var source = LoadCmsPagesSource();

        Assert.That(source.Assembly, Is.SameAs(typeof(Controllers.Admin.AdminContentController).Assembly));
    }

    [Test]
    public void CmsPagesSeed_ParsesAndEverySeedReferenceNamesAnItemInTheFile()
    {
        var json = LoadCmsPagesSource().Json;
        var document = JsonSerializer.Deserialize<ContentSeedDocument>(json, ContentFieldMerger.JsonOptions)!;
        var ids = document.Items!.Select(i => i.Id).ToHashSet();
        var referenced = SeedReferenceResolver.CollectReferences(JsonNode.Parse(json));

        Assert.Multiple(() =>
        {
            Assert.That(document.Items!.Select(i => i.ContentType), Is.SupersetOf(new[] { "routes", "pages", "contentzoneitems" }));
            Assert.That(ids, Has.Count.EqualTo(document.Items!.Count), "seed ids are unique");
            Assert.That(referenced, Is.Not.Empty);
            Assert.That(referenced, Is.SubsetOf(ids));
        });
    }
}
