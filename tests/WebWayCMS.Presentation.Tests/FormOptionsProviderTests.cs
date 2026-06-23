using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Presentation.Components.Widgets;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormOptionsProviderTests
{
	private static FormPropertyInfo ViewPicker(string? component) => new() { EditorType = EditorType.ViewPicker, ViewComponentName = component ?? string.Empty };
	private static FormPropertyInfo Entity(string? entityType) => new() { EditorType = EditorType.Guid, EntityType = entityType ?? string.Empty };

	private static FormOptionsProvider New(IAdminHandlerRegistry? handlers = null)
		=> new(handlers ?? Substitute.For<IAdminHandlerRegistry>());

	[Test]
	public void Constructor_Null_Throws()
		=> Assert.That(() => new FormOptionsProvider(null!), Throws.ArgumentNullException);

	[Test]
	public async Task ViewPicker_Layout_ReturnsBuiltInLayoutNames()
	{
		var options = await New().GetOptionsAsync(ViewPicker("Layout"));

		Assert.That(options.Select(o => o.Value), Is.EqualTo(LayoutWidget.LayoutViewNames));
	}

	[Test]
	public async Task ViewPicker_NonLayoutComponent_Empty()
		=> Assert.That(await New().GetOptionsAsync(ViewPicker("Article")), Is.Empty);

	[Test]
	public async Task ViewPicker_NoComponentName_Empty()
		=> Assert.That(await New().GetOptionsAsync(ViewPicker(null)), Is.Empty);

	[Test]
	public async Task NonPickerField_Empty()
		=> Assert.That(await New().GetOptionsAsync(new FormPropertyInfo { EditorType = EditorType.Text }), Is.Empty);

	[Test]
	public async Task Guid_NoEntityType_Empty()
		=> Assert.That(await New().GetOptionsAsync(Entity(null)), Is.Empty);

	[Test]
	public async Task Entity_ContentBlock_MapsIdTitle_SkippingInvalid()
	{
		var handlers = Substitute.For<IAdminHandlerRegistry>();
		var handler = Substitute.For<IAdminCrudHandler>();
		handlers.GetHandler("contentblocks").Returns(handler);
		handler.GetApiListAsync(Arg.Any<CancellationToken>()).Returns(new object[]
		{
			new { id = "g1", title = "Block 1" },
			new { id = "", title = "skip me" }, // empty id -> skipped
			new { other = "no id property" },   // missing id property -> skipped
			new { id = "g2", title = "" },       // empty title -> id used as label
		});

		var options = await New(handlers: handlers).GetOptionsAsync(Entity("ContentBlock"));

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(o => o.Value), Is.EqualTo(new[] { "g1", "g2" }));
			Assert.That(options[0].Label, Is.EqualTo("Block 1"));
			Assert.That(options[1].Label, Is.EqualTo("g2"));
		});
	}

	[TestCase("ContentZone", "contentzones")]
	[TestCase("Article", "articles")]
	public async Task Entity_PrimaryTypes_UseCorrectHandler(string entityType, string contentType)
	{
		var handlers = Substitute.For<IAdminHandlerRegistry>();
		var handler = Substitute.For<IAdminCrudHandler>();
		handlers.GetHandler(contentType).Returns(handler);
		handler.GetApiListAsync(Arg.Any<CancellationToken>()).Returns(new object[] { new { id = "x", title = "X" } });

		var options = await New(handlers: handlers).GetOptionsAsync(Entity(entityType));

		Assert.That(options.Single().Value, Is.EqualTo("x"));
	}

	[Test]
	public async Task Entity_ArticleList_UsesSecondaryList()
	{
		var handlers = Substitute.For<IAdminHandlerRegistry>();
		var handler = Substitute.For<IAdminCrudHandler>();
		handlers.GetHandler("articles").Returns(handler);
		handler.GetSecondaryApiListAsync("articlelists", Arg.Any<CancellationToken>())
			.Returns(new object[] { new { id = "l1", title = "List 1" } });

		var options = await New(handlers: handlers).GetOptionsAsync(Entity("ArticleList"));

		Assert.That(options.Single().Value, Is.EqualTo("l1"));
	}

	[Test]
	public async Task Entity_UnknownType_Empty()
		=> Assert.That(await New().GetOptionsAsync(Entity("Nope")), Is.Empty);

	[Test]
	public async Task Entity_HandlerMissing_Empty()
	{
		var handlers = Substitute.For<IAdminHandlerRegistry>();
		handlers.GetHandler(Arg.Any<string>()).Returns((IAdminCrudHandler?)null);

		Assert.That(await New(handlers: handlers).GetOptionsAsync(Entity("ContentBlock")), Is.Empty);
	}
}
