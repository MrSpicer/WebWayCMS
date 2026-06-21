using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Models.Article;
using WebWayCMS.Presentation.Components.Admin;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminArticlesPageTests
{
	private static ArticleViewModel Article(string title, bool withMaster = true, bool withDates = false) => new()
	{
		Id = Guid.NewGuid(),
		MasterId = withMaster ? Guid.NewGuid() : null,
		Version = 1,
		Title = title,
		Slug = title.ToLowerInvariant(),
		CreationDate = withDates ? DateTime.UtcNow : null,
		ModificationDate = withDates ? DateTime.UtcNow : null,
	};

	private static (BunitContext Ctx, IArticleModel ArticleModel) Build(ArticleListViewModel? list, params ArticleViewModel[] _)
	{
		var ctx = new BunitContext();
		var listModel = Substitute.For<IArticleListModel>();
		listModel.GetArticlesForListBySlugAsync("news", Arg.Any<CancellationToken>()).Returns(list);
		var articleModel = Substitute.For<IArticleModel>();
		ctx.Services.AddSingleton(listModel);
		ctx.Services.AddSingleton(articleModel);
		return (ctx, articleModel);
	}

	private static ArticleListViewModel ListWith(params ArticleViewModel[] articles) => new()
	{
		ArticleListTitle = "Company News",
		ArticleListSlug = "news",
		Articles = articles.ToList(),
	};

	private static IRenderedComponent<AdminArticlesPage> Render(BunitContext ctx)
		=> ctx.Render<AdminArticlesPage>(p => p.Add(c => c.Slug, "news"));

	[Test]
	public void ListNotFound_ShowsWarning()
	{
		var (ctx, _) = Build(null);
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.That(cut.Markup, Does.Contain("Article list not found"));
		}
	}

	[Test]
	public void EmptyList_ShowsEmptyState()
	{
		var (ctx, _) = Build(ListWith());
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.That(cut.Markup, Does.Contain("No articles yet"));
		}
	}

	[Test]
	public void WithArticles_RendersRows_VersionsLinkOnlyWhenMastered()
	{
		var mastered = Article("Alpha", withMaster: true, withDates: true);
		var (ctx, _) = Build(ListWith(mastered, Article("Beta", withMaster: false)));
		using (ctx)
		{
			var cut = Render(ctx);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Company News — Articles"));
				Assert.That(cut.Markup, Does.Contain("Alpha"));
				Assert.That(cut.Markup, Does.Contain("Beta"));
				Assert.That(cut.Markup, Does.Contain($"/admin/versions/articles/news/articles/{mastered.MasterId}")); // mastered -> version-history link
				Assert.That(cut.FindAll("a.is-info"), Has.Count.EqualTo(1)); // only the mastered row has Versions
				Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
			});
		}
	}

	[Test]
	public void ConfirmDelete_CallsModel_AndRemovesRow()
	{
		var article = Article("Alpha");
		var (ctx, articleModel) = Build(ListWith(article));
		articleModel.DeleteAsync(article.Id!.Value, Arg.Any<CancellationToken>()).Returns(true);
		using (ctx)
		{
			var cut = Render(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.confirm-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("Alpha"));
				Assert.That(cut.Markup, Does.Contain("No articles yet"));
			});
			articleModel.Received(1).DeleteAsync(article.Id!.Value, Arg.Any<CancellationToken>());
		}
	}

	[Test]
	public void CancelDelete_ClosesModal_KeepsRow()
	{
		var (ctx, _) = Build(ListWith(Article("Alpha")));
		using (ctx)
		{
			var cut = Render(ctx);
			cut.Find("tbody button.is-danger").Click();
			cut.Find("button.cancel-delete").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("modal is-active"));
				Assert.That(cut.Markup, Does.Contain("Alpha"));
			});
		}
	}
}
