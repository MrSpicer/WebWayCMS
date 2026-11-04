using Microsoft.AspNetCore.Mvc.ViewComponents;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentZoneViewComponentTests
{
    private IContentZoneModel _model = null!;
    private IWidgetRegistry _registry = null!;
    private ContentZoneViewComponent _component = null!;
    private Microsoft.AspNetCore.Http.DefaultHttpContext _http = null!;

    [SetUp]
    public void SetUp()
    {
        _model = Substitute.For<IContentZoneModel>();
        _registry = Substitute.For<IWidgetRegistry>();
        _component = new ContentZoneViewComponent(_model, _registry);
        _http = ViewComponentHarness.Attach(_component);
    }

    private static ContentZoneViewModel ZoneVm(bool withItems = false, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Z",
        ZoneObjects = withItems ? new List<ContentZoneObject> { new() } : new List<ContentZoneObject>()
    };

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ContentZoneViewComponent(null!, _registry), Throws.ArgumentNullException);
            Assert.That(() => new ContentZoneViewComponent(_model, null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public async Task ZoneId_DirectLookup_RendersZone()
    {
        var zoneId = Guid.NewGuid();
        _model.GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneId: zoneId);

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task WhitespaceZoneName_RendersEmpty()
    {
        Assert.That(await _component.InvokeAsync(zoneName: "  "), Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task PageScoped_TopLevelSlot_GetsOrCreatesByPageSlot()
    {
        var pageNodeId = Guid.NewGuid();
        _http.Items["CMS:PageData"] = new PageDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = pageNodeId } }
        };
        _model.GetOrCreateViewModelByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Main");

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task PageScoped_NestedSlot_GetsOrCreatesByItemSlot()
    {
        var pageNodeId = Guid.NewGuid();
        _http.Items["CMS:PageData"] = new PageDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = pageNodeId } }
        };
        _component.ViewComponentContext.ViewData["ContentZone:ParentItemId"] = Guid.NewGuid();
        _model.GetOrCreateViewModelByItemSlotAsync(Arg.Any<Guid>(), "Sub", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Sub");

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task NestedSlot_WithoutPageData_GetsOrCreatesByItemSlot()
    {
        _component.ViewComponentContext.ViewData["ContentZone:ParentItemId"] = Guid.NewGuid();
        _model.GetOrCreateViewModelByItemSlotAsync(Arg.Any<Guid>(), "Sub", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Sub");

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task InheritedEditMode_FromViewData_RendersEditView()
    {
        _component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.Edit;
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm());
        _registry.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<WidgetRegistrationInfo>>());

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Edit"));
            Assert.That(_component.ViewComponentContext.ViewData["ContentZone:RenderMode"], Is.EqualTo(WidgetRenderMode.Edit));
            Assert.That(_component.ViewComponentContext.ViewData.ContainsKey("ComponentsByCategory"), Is.True);
        });
    }

    [Test]
    public async Task GlobalZone_GetsOrCreatesByName()
    {
        _model.GetOrCreateViewModelAsync("Footer", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        var result = await _component.InvokeAsync(zoneName: "Footer", IsGlobal: true);

        Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
    }

    [Test]
    public async Task NullViewModel_BuildsEmptyAndRendersEmptyContent()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns((ContentZoneViewModel)null!);

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task EditMode_RendersEditViewAndSetsComponents()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm());
        _registry.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<WidgetRegistrationInfo>>());

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true, editMode: true);

        Assert.Multiple(() =>
        {
            Assert.That(ViewComponentHarness.ViewName(result), Is.EqualTo("Edit"));
            Assert.That(_component.ViewComponentContext.ViewData["ContentZone:RenderMode"], Is.EqualTo(WidgetRenderMode.Edit));
            Assert.That(_component.ViewComponentContext.ViewData.ContainsKey("ComponentsByCategory"), Is.True);
        });
    }

    // ─── edit mode: owning page (drag-and-drop scope) ─────────────────────────

    private ContentZoneViewModel EditVm(object result)
        => (ContentZoneViewModel)((ViewViewComponentResult)result).ViewData!.Model!;

    [Test]
    public async Task EditMode_ZoneIdLookup_ResolvesOwnerPage()
    {
        var zoneId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        _model.GetViewModelByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns(ZoneVm(id: zoneId));
        _model.GetParentPageNodeForZoneAsync(zoneId, Arg.Any<CancellationToken>()).Returns(pageId);

        var result = await _component.InvokeAsync(zoneName: "Main", zoneId: zoneId, editMode: true);

        Assert.Multiple(() =>
        {
            Assert.That(EditVm(result).OwnerPageNodeId, Is.EqualTo(pageId));
            Assert.That(_component.ViewComponentContext.ViewData["ContentZone:OwnerPageId"], Is.EqualTo(pageId));
        });
    }

    [Test]
    public async Task EditMode_ZoneIdLookupMissingZone_HasNoOwner()
    {
        _model.GetViewModelByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentZoneViewModel?)null);

        var result = await _component.InvokeAsync(zoneName: "Main", zoneId: Guid.NewGuid(), editMode: true);

        Assert.That(EditVm(result).OwnerPageNodeId, Is.Null);
        await _model.DidNotReceive().GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditMode_PageSlot_OwnerIsThePage()
    {
        var pageNodeId = Guid.NewGuid();
        _http.Items["CMS:PageData"] = new PageDTO { Version = new ContentVersion { Node = new ContentNode { Id = pageNodeId } } };
        _model.GetOrCreateViewModelByPageSlotAsync(pageNodeId, "Main", Arg.Any<CancellationToken>()).Returns(ZoneVm());

        var result = await _component.InvokeAsync(zoneName: "Main", editMode: true);

        Assert.That(EditVm(result).OwnerPageNodeId, Is.EqualTo(pageNodeId));
        await _model.DidNotReceive().GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditMode_NestedZone_InheritsOwnerOrResolvesIt()
    {
        var inherited = Guid.NewGuid();
        var resolved = Guid.NewGuid();
        var vm = ZoneVm();
        _model.GetOrCreateViewModelByItemSlotAsync(Arg.Any<Guid>(), "Left", Arg.Any<CancellationToken>()).Returns(vm);
        _model.GetParentPageNodeForZoneAsync(vm.Id, Arg.Any<CancellationToken>()).Returns(resolved);

        _component.ViewComponentContext.ViewData["ContentZone:ParentItemId"] = Guid.NewGuid();
        _component.ViewComponentContext.ViewData["ContentZone:OwnerPageId"] = inherited;
        var inheritedResult = EditVm(await _component.InvokeAsync(zoneName: "Left", editMode: true)).OwnerPageNodeId;

        // A null owner (the enclosing zone is global) is a resolved answer: inherited, not looked up.
        _component.ViewComponentContext.ViewData["ContentZone:OwnerPageId"] = null;
        var inheritedNullResult = EditVm(await _component.InvokeAsync(zoneName: "Left", editMode: true)).OwnerPageNodeId;
        await _model.DidNotReceive().GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        _component.ViewComponentContext.ViewData.Remove("ContentZone:OwnerPageId");
        var resolvedResult = EditVm(await _component.InvokeAsync(zoneName: "Left", editMode: true)).OwnerPageNodeId;

        Assert.Multiple(() =>
        {
            Assert.That(inheritedResult, Is.EqualTo(inherited));
            Assert.That(inheritedNullResult, Is.Null);
            Assert.That(resolvedResult, Is.EqualTo(resolved));
        });
    }

    [Test]
    public async Task NestedSlot_WithoutOwningItem_RendersNothingInsteadOfAliasingThePageSlot()
    {
        // A zone view (e.g. a host override) that never set ContentZone:ParentItemId: resolving the
        // page slot by name would hand back the enclosing zone and recurse.
        var pageNodeId = Guid.NewGuid();
        _http.Items["CMS:PageData"] = new PageDTO { Version = new ContentVersion { Node = new ContentNode { Id = pageNodeId } } };
        _component.ViewComponentContext.ViewData["ContentZone:RenderMode"] = WidgetRenderMode.View;

        var result = await _component.InvokeAsync(zoneName: "Main");

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
        await _model.DidNotReceive().GetOrCreateViewModelByPageSlotAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _model.DidNotReceive().GetOrCreateViewModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditMode_GlobalZone_HasNoOwnerWithoutLookup()
    {
        _model.GetOrCreateViewModelAsync("Header", Arg.Any<CancellationToken>()).Returns(ZoneVm());

        var result = await _component.InvokeAsync(zoneName: "Header", IsGlobal: true, editMode: true);

        Assert.That(EditVm(result).OwnerPageNodeId, Is.Null);
        await _model.DidNotReceive().GetParentPageNodeForZoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EditMode_NameFallbackZone_ResolvesOwner()
    {
        var vm = ZoneVm();
        _model.GetOrCreateViewModelAsync("Header", Arg.Any<CancellationToken>()).Returns(vm);
        _model.GetParentPageNodeForZoneAsync(vm.Id, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await _component.InvokeAsync(zoneName: "Header", editMode: true);

        Assert.That(EditVm(result).OwnerPageNodeId, Is.Null);
        await _model.Received(1).GetParentPageNodeForZoneAsync(vm.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NoItems_NotEditMode_RendersEmpty()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: false));

        var result = await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
    }

    [Test]
    public async Task PublicMode_SetsViewRenderModeInViewData()
    {
        _model.GetOrCreateViewModelAsync("X", Arg.Any<CancellationToken>()).Returns(ZoneVm(withItems: true));

        await _component.InvokeAsync(zoneName: "X", IsGlobal: true);

        Assert.That(_component.ViewComponentContext.ViewData["ContentZone:RenderMode"], Is.EqualTo(WidgetRenderMode.View));
    }
}
