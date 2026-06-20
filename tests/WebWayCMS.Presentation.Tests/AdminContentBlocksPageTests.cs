using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Presentation.Components.Admin;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminContentBlocksPageTests
{
	private static ContentBlockItemViewModel Item(string title) =>
		new() { Id = Guid.NewGuid(), MasterId = Guid.NewGuid(), Version = 1, Title = title, Slug = title.ToLowerInvariant() };

	private static IContentBlockModel ModelWith(params ContentBlockItemViewModel[] items)
	{
		var model = Substitute.For<IContentBlockModel>();
		model.GetContentBlockIndexAsync(Arg.Any<CancellationToken>())
			.Returns(new ContentBlockIndexViewModel { ContentBlocks = items.ToList() });
		return model;
	}

	[Test]
	public void EmptyList_ShowsEmptyState()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith());

		var cut = ctx.Render<AdminContentBlocksPage>();

		Assert.That(cut.Markup, Does.Contain("No content blocks yet"));
	}

	[Test]
	public void WithItems_RendersRows_NoModal()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith(Item("Alpha"), Item("Beta")));

		var cut = ctx.Render<AdminContentBlocksPage>();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Alpha"));
			Assert.That(cut.Markup, Does.Contain("Beta"));
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
		});
	}

	[Test]
	public void ClickDelete_ShowsConfirmModal()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith(Item("Alpha")));

		var cut = ctx.Render<AdminContentBlocksPage>();
		cut.Find("tbody button.is-danger").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Contain("modal is-active"));
			Assert.That(cut.Markup, Does.Contain("Are you sure"));
		});
	}

	[Test]
	public void ConfirmDelete_RemovesItem_AndCallsModel()
	{
		var item = Item("Alpha");
		var model = ModelWith(item);
		model.DeleteAsync(item.Id, Arg.Any<CancellationToken>()).Returns(true);

		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(model);

		var cut = ctx.Render<AdminContentBlocksPage>();
		cut.Find("tbody button.is-danger").Click();
		cut.Find("button.confirm-delete").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Not.Contain("Alpha"));
			Assert.That(cut.Markup, Does.Contain("No content blocks yet"));
		});
		model.Received(1).DeleteAsync(item.Id, Arg.Any<CancellationToken>());
	}

	[Test]
	public void CancelDelete_ClosesModal_KeepsItem()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith(Item("Alpha")));

		var cut = ctx.Render<AdminContentBlocksPage>();
		cut.Find("tbody button.is-danger").Click();
		cut.Find("button.cancel-delete").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
			Assert.That(cut.Markup, Does.Contain("Alpha"));
		});
	}
}
