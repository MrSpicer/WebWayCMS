using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.Article;
using WebWayCMS.Presentation.Components.Admin;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminArticleListsPageTests
{
	private static ArticleListItemViewModel Item(string title) =>
		new() { Id = Guid.NewGuid(), MasterId = Guid.NewGuid(), Version = 1, Title = title, Slug = title.ToLowerInvariant(), ArticleCount = 2 };

	private static IArticleListModel ModelWith(params ArticleListItemViewModel[] items)
	{
		var model = Substitute.For<IArticleListModel>();
		model.GetArticleListIndexAsync(Arg.Any<CancellationToken>())
			.Returns(new ArticleListIndexViewModel { ArticleLists = items.ToList() });
		return model;
	}

	[Test]
	public void EmptyList_ShowsEmptyState()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith());

		var cut = ctx.Render<AdminArticleListsPage>();

		Assert.That(cut.Markup, Does.Contain("No article lists yet"));
	}

	[Test]
	public void WithItems_RendersRows_WithArticleCount_NoModal()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith(Item("Alpha"), Item("Beta")));

		var cut = ctx.Render<AdminArticleListsPage>();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Contain("Alpha"));
			Assert.That(cut.Markup, Does.Contain("Beta"));
			Assert.That(cut.Markup, Does.Contain("/admin/article-lists/alpha/articles")); // Manage Articles link (Blazor child list)
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
		});
	}

	[Test]
	public void ClickDelete_ShowsConfirmModal()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith(Item("Alpha")));

		var cut = ctx.Render<AdminArticleListsPage>();
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
		model.DeleteArticleListAsync(item.Id, Arg.Any<CancellationToken>()).Returns(true);

		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(model);

		var cut = ctx.Render<AdminArticleListsPage>();
		cut.Find("tbody button.is-danger").Click();
		cut.Find("button.confirm-delete").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Not.Contain("Alpha"));
			Assert.That(cut.Markup, Does.Contain("No article lists yet"));
		});
		model.Received(1).DeleteArticleListAsync(item.Id, Arg.Any<CancellationToken>());
	}

	[Test]
	public void CancelDelete_ClosesModal_KeepsItem()
	{
		using var ctx = new BunitContext();
		ctx.Services.AddSingleton(ModelWith(Item("Alpha")));

		var cut = ctx.Render<AdminArticleListsPage>();
		cut.Find("tbody button.is-danger").Click();
		cut.Find("button.cancel-delete").Click();

		Assert.Multiple(() =>
		{
			Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
			Assert.That(cut.Markup, Does.Contain("Alpha"));
		});
	}
}
