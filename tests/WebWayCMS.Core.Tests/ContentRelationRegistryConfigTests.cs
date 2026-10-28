using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Pages;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class ContentRelationRegistryConfigTests
{
    private sealed class WidgetConfig
    {
        [ContentRelation("images")]
        public Guid? ImageId { get; set; }

        [ContentRelation("faqs", OnTargetDelete = RelationDeletePolicy.Detach)]
        public Guid? FaqId { get; set; }
    }

    private sealed class PageConfig
    {
        [ContentRelation("faqs", OnTargetDelete = RelationDeletePolicy.Detach)]
        public Guid? FaqId { get; set; }
    }

    private sealed class InvalidWidgetConfig
    {
        [ContentRelation("images", Kind = ContentRelationKind.Containment)]
        public List<Guid> Images { get; set; } = [];
    }

    private sealed class UnknownTargetConfig
    {
        [ContentRelation("nothing")]
        public Guid Thing { get; set; }
    }

    private sealed class PlainModel
    {
    }

    private static IAdminCrudHandler Handler(string contentType, object upsertModel = null!)
    {
        var handler = Substitute.For<IAdminCrudHandler>();
        handler.ContentType.Returns(contentType);
        handler.CreateEmptyUpsertViewModel().Returns(upsertModel ?? new PlainModel());
        return handler;
    }

    private static IWidgetRegistry Widgets(params Type[] configurationTypes)
    {
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetAllComponents().Returns(configurationTypes
            .Select((type, i) => new WidgetRegistrationInfo
            {
                Name = "Widget" + i,
                ConfigurationTypeName = type.AssemblyQualifiedName
            })
            .ToList());
        return registry;
    }

    private static IPageControllerRegistry Pages(params Type[] configurationTypes)
    {
        var registry = Substitute.For<IPageControllerRegistry>();
        registry.GetAllControllers().Returns(configurationTypes
            .Select(type => new PageControllerInfo { Name = type.Name, ConfigurationType = type })
            .ToList());
        return registry;
    }

    private static ContentRelationRegistry Registry(
        IWidgetRegistry? widgets = null, IPageControllerRegistry? pages = null, params IAdminCrudHandler[] handlers)
        => new(new HandlerRegistryStub(handlers), widgets, pages);

    [Test]
    public void WidgetConfigurations_AreDeclaredUnderTheZoneItemTypeWithTheConfigPrefix()
    {
        var registry = Registry(Widgets(typeof(WidgetConfig)), null, Handler("images"), Handler("faqs"));

        registry.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Get(ContentRelationRegistry.ContentZoneItemContentType).Select(d => d.Name),
                Is.EqualTo(new[] { "config.ImageId", "config.FaqId" }));
            Assert.That(registry.GetPolicy(ContentRelationRegistry.ContentZoneItemContentType, "config.FaqId"),
                Is.EqualTo(RelationDeletePolicy.Detach));
            Assert.That(registry.GetPolicy(ContentRelationRegistry.ContentZoneItemContentType, "config.ImageId"),
                Is.EqualTo(RelationDeletePolicy.Restrict));
            Assert.That(registry.Find(ContentRelationRegistry.ContentZoneItemContentType, "config.ImageId")!.ModelType,
                Is.EqualTo(typeof(WidgetConfig)));
        });
    }

    [Test]
    public void PageConfigurations_JoinThePageDeclarations()
    {
        var registry = Registry(null, Pages(typeof(PageConfig)), Handler("pages"), Handler("faqs"));

        registry.Validate();

        Assert.That(registry.Get("pages").Select(d => d.Name), Does.Contain("config.FaqId"));
    }

    [Test]
    public void DuplicateConfigurationTypes_AreDeclaredOnce()
    {
        var registry = Registry(Widgets(typeof(WidgetConfig), typeof(WidgetConfig)), null, Handler("images"), Handler("faqs"));

        registry.Validate();

        Assert.That(registry.Get(ContentRelationRegistry.ContentZoneItemContentType)
            .Count(d => d.Name == "config.ImageId"), Is.EqualTo(1));
    }

    [Test]
    public void InvalidWidgetDeclarations_FailTheBoot()
    {
        var manyContainment = Registry(Widgets(typeof(InvalidWidgetConfig)), null, Handler("images"));
        var unknownTarget = Registry(Widgets(typeof(UnknownTargetConfig)), null, Handler("images"));

        Assert.Multiple(() =>
        {
            Assert.That(() => manyContainment.Validate(), Throws.InvalidOperationException.With.Message.Contains("containment"));
            Assert.That(() => unknownTarget.Validate(), Throws.InvalidOperationException.With.Message.Contains("is not a registered content type"));
        });
    }

    [Test]
    public void NoWidgetsOrPages_DeclaresNothing()
    {
        var registry = Registry(null, null, Handler("images"));
        registry.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(registry.Get(ContentRelationRegistry.ContentZoneItemContentType), Is.Empty);
            Assert.That(registry.Get("pages"), Is.Empty);
        });
    }
}
