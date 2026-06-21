using System.ComponentModel.DataAnnotations;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminChildUpsertTests
{
	public sealed class DemoChildVm
	{
		[Required]
		[FormProperty(Label = "Title", EditorType = EditorType.Text, IsRequired = true, Order = 1)]
		public string Title { get; set; } = string.Empty;

		[FormProperty(Label = "View", EditorType = EditorType.ViewPicker, ViewComponentName = "Demo", Order = 2)]
		public string? ViewName { get; set; }
	}

	private const string Parent = "list-slug";

	private sealed class Harness
	{
		public required BunitContext Ctx { get; init; }
		public required IAdminCrudChildHandler Child { get; init; }
		public required IFormOptionsProvider Options { get; init; }
		public NavigationManager Nav => Ctx.Services.GetRequiredService<NavigationManager>();
	}

	private static Harness Build(bool registerHandler = true)
	{
		var ctx = new BunitContext();
		var child = Substitute.For<IAdminCrudChildHandler>();
		child.ChildDisplayName.Returns("Article");

		var handler = Substitute.For<IAdminCrudHandler>();
		handler.ChildHandler.Returns(child);

		var registry = Substitute.For<IAdminHandlerRegistry>();
		registry.GetHandler("articles").Returns(registerHandler ? handler : null);

		var options = Substitute.For<IFormOptionsProvider>();
		options.GetOptionsAsync(Arg.Any<FormPropertyInfo>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(Array.Empty<FormOption>()));

		ctx.Services.AddSingleton(registry);
		ctx.Services.AddSingleton(options);
		return new Harness { Ctx = ctx, Child = child, Options = options };
	}

	private static IRenderedComponent<AdminChildUpsert> Render(Harness h, Guid? id = null)
		=> h.Ctx.Render<AdminChildUpsert>(p => p
			.Add(c => c.ContentType, "articles")
			.Add(c => c.ParentKey, Parent)
			.Add(c => c.Id, id)
			.Add(c => c.ListUrl, "/admin/article-lists/list-slug/articles"));

	[Test]
	public void NoChildHandler_ShowsError()
	{
		var h = Build(registerHandler: false);
		using (h.Ctx)
		{
			var cut = Render(h);
			Assert.That(cut.Markup, Does.Contain("No child handler available for content type 'articles'"));
		}
	}

	[Test]
	public void CreateMode_RendersNewForm_AndLoadsPickerOptions()
	{
		var h = Build();
		h.Child.GetChildUpsertViewModelAsync(Parent, null, Arg.Any<CancellationToken>())
			.Returns((object?)new DemoChildVm());
		h.Options.GetOptionsAsync(Arg.Is<FormPropertyInfo>(p => p.Name == "ViewName"), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(new[] { new FormOption("Demo", "Demo View") }));

		using (h.Ctx)
		{
			var cut = Render(h);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("New Article"));
				Assert.That(cut.Markup, Does.Contain("data-prop=\"Title\""));
				Assert.That(cut.Markup, Does.Contain(">Demo View</option>"));
			});
		}
	}

	[Test]
	public void NotFound_ShowsWarning()
	{
		var h = Build();
		var id = Guid.NewGuid();
		h.Child.GetChildUpsertViewModelAsync(Parent, id, Arg.Any<CancellationToken>())
			.Returns((object?)null);

		using (h.Ctx)
		{
			var cut = Render(h, id);
			Assert.That(cut.Markup, Does.Contain("was not found"));
		}
	}

	[Test]
	public void EditMode_RendersPrefilledEditForm()
	{
		var h = Build();
		var id = Guid.NewGuid();
		h.Child.GetChildUpsertViewModelAsync(Parent, id, Arg.Any<CancellationToken>())
			.Returns((object?)new DemoChildVm { Title = "Existing" });

		using (h.Ctx)
		{
			var cut = Render(h, id);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Edit Article"));
				Assert.That(cut.Markup, Does.Contain("value=\"Existing\""));
			});
		}
	}

	[Test]
	public void RestoreMode_LoadsHistoricalChildVersion_AsEdit()
	{
		var h = Build();
		var restoreId = Guid.NewGuid();
		h.Child.GetChildRestoreVersionViewModelAsync(Parent, restoreId, Arg.Any<CancellationToken>())
			.Returns((object?)new DemoChildVm { Title = "Restored" });
		using (h.Ctx)
		{
			var cut = h.Ctx.Render<AdminChildUpsert>(p => p
				.Add(c => c.ContentType, "articles")
				.Add(c => c.ParentKey, Parent)
				.Add(c => c.RestoreId, restoreId)
				.Add(c => c.ListUrl, "/admin/article-lists/list-slug/articles"));

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Edit Article"));
				Assert.That(cut.Markup, Does.Contain("value=\"Restored\""));
			});
		}
	}

	[Test]
	public void Save_Valid_CallsHandler_AndNavigatesToList()
	{
		var h = Build();
		h.Child.GetChildUpsertViewModelAsync(Parent, null, Arg.Any<CancellationToken>())
			.Returns((object?)new DemoChildVm());
		h.Child.SaveChildUpsertAsync(Parent, Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(new AdminSaveResult(true));

		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("input[data-prop=\"Title\"]").Change("My Article");
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				h.Child.Received(1).SaveChildUpsertAsync(Parent, Arg.Is<object>(m => ((DemoChildVm)m).Title == "My Article"), Arg.Any<CancellationToken>());
				Assert.That(h.Nav.Uri, Does.EndWith("/admin/article-lists/list-slug/articles"));
			});
		}
	}

	[Test]
	public void Save_Invalid_ShowsValidationError_AndDoesNotSave()
	{
		var h = Build();
		h.Child.GetChildUpsertViewModelAsync(Parent, null, Arg.Any<CancellationToken>())
			.Returns((object?)new DemoChildVm()); // Title empty -> [Required] fails

		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Find(".save-error").TextContent, Is.Not.Empty);
				h.Child.DidNotReceive().SaveChildUpsertAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
				Assert.That(h.Nav.Uri, Does.Not.Contain("articles"));
			});
		}
	}

	[Test]
	public void Save_HandlerFailure_ShowsError_AndStays()
	{
		var h = Build();
		h.Child.GetChildUpsertViewModelAsync(Parent, null, Arg.Any<CancellationToken>())
			.Returns((object?)new DemoChildVm());
		h.Child.SaveChildUpsertAsync(Parent, Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(new AdminSaveResult(false, "Boom"));

		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("input[data-prop=\"Title\"]").Change("My Article");
			cut.Find(".save-content").Click();

			Assert.That(cut.Find(".save-error").TextContent, Does.Contain("Boom"));
		}
	}
}
