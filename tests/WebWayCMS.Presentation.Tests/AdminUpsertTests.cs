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
public class AdminUpsertTests
{
	public sealed class DemoVm
	{
		[Required]
		[FormProperty(Label = "Title", EditorType = EditorType.Text, IsRequired = true, Order = 1)]
		public string Title { get; set; } = string.Empty;

		[FormProperty(Label = "View", EditorType = EditorType.ViewPicker, ViewComponentName = "Demo", Order = 2)]
		public string? ViewName { get; set; }
	}

	private sealed class Harness
	{
		public required BunitContext Ctx { get; init; }
		public required IAdminCrudHandler Handler { get; init; }
		public required IFormOptionsProvider Options { get; init; }
		public NavigationManager Nav => Ctx.Services.GetRequiredService<NavigationManager>();
	}

	private static Harness Build(bool registerHandler = true)
	{
		var ctx = new BunitContext();
		var handler = Substitute.For<IAdminCrudHandler>();
		handler.DisplayName.Returns("Content Block");

		var registry = Substitute.For<IAdminHandlerRegistry>();
		registry.GetHandler("contentblocks").Returns(registerHandler ? handler : null);

		var options = Substitute.For<IFormOptionsProvider>();
		options.GetOptionsAsync(Arg.Any<FormPropertyInfo>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(Array.Empty<FormOption>()));

		ctx.Services.AddSingleton(registry);
		ctx.Services.AddSingleton(options);
		return new Harness { Ctx = ctx, Handler = handler, Options = options };
	}

	private static IRenderedComponent<AdminUpsert> Render(Harness h, Guid? id = null)
		=> h.Ctx.Render<AdminUpsert>(p => p
			.Add(c => c.ContentType, "contentblocks")
			.Add(c => c.Id, id)
			.Add(c => c.ListUrl, "/admin/blocks"));

	[Test]
	public void UnknownContentType_ShowsError()
	{
		var h = Build(registerHandler: false);
		using (h.Ctx)
		{
			var cut = Render(h);
			Assert.That(cut.Markup, Does.Contain("No admin handler registered for content type 'contentblocks'"));
		}
	}

	[Test]
	public void CreateMode_RendersNewForm_AndLoadsPickerOptions()
	{
		var h = Build();
		h.Handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)new DemoVm());
		// ViewName is a picker -> options provided (covers the "options present" branch); Title -> empty.
		h.Options.GetOptionsAsync(Arg.Is<FormPropertyInfo>(p => p.Name == "ViewName"), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(new[] { new FormOption("Demo", "Demo View") }));

		using (h.Ctx)
		{
			var cut = Render(h);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("New Content Block"));
				Assert.That(cut.Markup, Does.Contain("data-prop=\"Title\""));
				Assert.That(cut.Markup, Does.Contain(">Demo View</option>"));
			});
		}
	}

	[Test]
	public void CreateMode_NullViewModel_FallsBackToCreateEmpty()
	{
		var h = Build();
		h.Handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)null);
		h.Handler.CreateEmptyUpsertViewModel().Returns(new DemoVm());

		using (h.Ctx)
		{
			var cut = Render(h);
			Assert.That(cut.Markup, Does.Contain("data-prop=\"Title\""));
			h.Handler.Received(1).CreateEmptyUpsertViewModel();
		}
	}

	[Test]
	public void EditMode_NotFound_ShowsWarning()
	{
		var h = Build();
		var id = Guid.NewGuid();
		h.Handler.GetUpsertViewModelAsync(id, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
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
		h.Handler.GetUpsertViewModelAsync(id, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)new DemoVm { Title = "Existing" });

		using (h.Ctx)
		{
			var cut = Render(h, id);
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Edit Content Block"));
				Assert.That(cut.Markup, Does.Contain("value=\"Existing\""));
			});
		}
	}

	[Test]
	public void Save_Valid_CallsHandler_AndNavigatesToList()
	{
		var h = Build();
		h.Handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)new DemoVm());
		h.Handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(new AdminSaveResult(true));

		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("input[data-prop=\"Title\"]").Change("My Block");
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				h.Handler.Received(1).SaveUpsertAsync(Arg.Is<object>(m => ((DemoVm)m).Title == "My Block"), Arg.Any<CancellationToken>());
				Assert.That(h.Nav.Uri, Does.EndWith("/admin/blocks"));
			});
		}
	}

	[Test]
	public void Save_Invalid_ShowsValidationError_AndDoesNotSave()
	{
		var h = Build();
		h.Handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)new DemoVm()); // Title empty -> [Required] fails

		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Find(".save-error").TextContent, Is.Not.Empty);
				h.Handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
				Assert.That(h.Nav.Uri, Does.Not.Contain("admin/blocks"));
			});
		}
	}

	[Test]
	public void Save_HandlerFailure_ShowsError_AndStays()
	{
		var h = Build();
		h.Handler.GetUpsertViewModelAsync(null, Arg.Any<Microsoft.AspNetCore.Http.IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)new DemoVm());
		h.Handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(new AdminSaveResult(false, "Boom"));

		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("input[data-prop=\"Title\"]").Change("My Block");
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Find(".save-error").TextContent, Does.Contain("Boom"));
				Assert.That(h.Nav.Uri, Does.Not.Contain("admin/blocks"));
			});
		}
	}
}
