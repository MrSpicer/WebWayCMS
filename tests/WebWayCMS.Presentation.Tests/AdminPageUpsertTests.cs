using System.ComponentModel.DataAnnotations;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Models.Page;
using WebWayCMS.Pages;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Rendering;
using WebWayCMS.Services;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class AdminPageUpsertTests
{
	public sealed class DemoConfig
	{
		[FormProperty(Label = "Heading", EditorType = EditorType.Text, Order = 1)]
		public string Heading { get; set; } = string.Empty;

		[FormProperty(Label = "Pick", EditorType = EditorType.ViewPicker, ViewComponentName = "X", Order = 2)]
		public string? Pick { get; set; }
	}

	private static PageControllerInfo BlogController() => new()
	{
		Name = "Blog",
		DisplayName = "Blog",
		Description = "A blog page",
		ConfigurationType = typeof(DemoConfig),
		Properties = FormPropertyBuilder.BuildPropertyInfos(typeof(DemoConfig)).ToList(),
	};

	private static PageControllerInfo SimpleController() => new()
	{
		Name = "Simple",
		DisplayName = "Simple",
		ConfigurationType = null, // no config
	};

	private sealed class Harness
	{
		public required BunitContext Ctx { get; init; }
		public required IAdminCrudHandler Handler { get; init; }
		public required IPageControllerRegistry Controllers { get; init; }
		public required IViewDiscoveryService Views { get; init; }
		public required IFormOptionsProvider Options { get; init; }
		public NavigationManager Nav => Ctx.Services.GetRequiredService<NavigationManager>();
	}

	private static Harness Build()
	{
		var ctx = new BunitContext();
		var handler = Substitute.For<IAdminCrudHandler>();
		var registry = Substitute.For<IAdminHandlerRegistry>();
		registry.GetHandler("pages").Returns(handler);

		var controllers = Substitute.For<IPageControllerRegistry>();
		controllers.GetAllControllers().Returns(new[] { BlogController(), SimpleController() });

		var views = Substitute.For<IViewDiscoveryService>();
		views.GetControllerViews(Arg.Any<string>()).Returns(new[] { "Default", "Wide" });

		var options = Substitute.For<IFormOptionsProvider>();
		options.GetOptionsAsync(Arg.Any<FormPropertyInfo>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(Array.Empty<FormOption>()));

		ctx.Services.AddSingleton(registry);
		ctx.Services.AddSingleton(controllers);
		ctx.Services.AddSingleton(views);
		ctx.Services.AddSingleton(options);
		return new Harness { Ctx = ctx, Handler = handler, Controllers = controllers, Views = views, Options = options };
	}

	private static IRenderedComponent<AdminPageUpsert> Render(Harness h, Guid? id = null, string? parentRoute = null)
		=> h.Ctx.Render<AdminPageUpsert>(p => p
			.Add(c => c.Id, id)
			.Add(c => c.ParentRoute, parentRoute));

	private static void ReturnsModel(Harness h, PageUpsertViewModel? model)
		=> h.Handler.GetUpsertViewModelAsync(Arg.Any<Guid?>(), Arg.Any<IQueryCollection>(), Arg.Any<CancellationToken>())
			.Returns((object?)model);

	[Test]
	public void EditMode_NotFound_ShowsWarning()
	{
		var h = Build();
		ReturnsModel(h, null);
		using (h.Ctx)
		{
			Assert.That(Render(h, Guid.NewGuid()).Markup, Does.Contain("page was not found"));
		}
	}

	[Test]
	public void CreateMode_RendersControllerPicker_NoConfigYet()
	{
		var h = Build();
		ReturnsModel(h, new PageUpsertViewModel());
		using (h.Ctx)
		{
			var cut = Render(h, parentRoute: "/products");
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Create Page"));
				Assert.That(cut.Markup, Does.Contain("controller-picker"));
				Assert.That(cut.Markup, Does.Contain(">Blog - A blog page</option>"));
				Assert.That(cut.Markup, Does.Contain("data-prop=\"Route\"")); // standard field
				Assert.That(cut.Markup, Does.Not.Contain("Configuration")); // no controller selected
			});
		}
	}

	[Test]
	public void EditMode_WithController_RendersConfig_FromJson_AndViews()
	{
		var h = Build();
		h.Controllers.GetByName("Blog").Returns(BlogController());
		ReturnsModel(h, new PageUpsertViewModel { Route = "/blog", ControllerName = "Blog", ConfigurationJson = "{\"Heading\":\"Hello\"}" });
		using (h.Ctx)
		{
			var cut = Render(h, Guid.NewGuid());
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Edit Page"));
				Assert.That(cut.Markup, Does.Contain("Configuration"));
				Assert.That(cut.Markup, Does.Contain("value=\"Hello\"")); // deserialized config value
				Assert.That(cut.Markup, Does.Contain(">Wide</option>")); // available views
			});
		}
	}

	[Test]
	public void SelectController_LoadsDefaultConfig()
	{
		var h = Build();
		h.Controllers.GetByName("Blog").Returns(BlogController());
		h.Controllers.CreateDefaultConfiguration("Blog").Returns(new DemoConfig());
		ReturnsModel(h, new PageUpsertViewModel());
		using (h.Ctx)
		{
			var cut = Render(h);
			Assert.That(cut.Markup, Does.Not.Contain("Configuration"));

			cut.Find("select.controller-picker").Change("Blog");
			Assert.That(cut.Markup, Does.Contain("Configuration")); // default config created -> section appears
		}
	}

	[Test]
	public void SelectController_UnknownName_NoConfig()
	{
		var h = Build();
		h.Controllers.GetByName("Ghost").Returns((PageControllerInfo?)null);
		ReturnsModel(h, new PageUpsertViewModel());
		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("select.controller-picker").Change("Ghost");
			Assert.That(cut.Markup, Does.Not.Contain("Configuration"));
		}
	}

	[Test]
	public void SelectController_WithoutConfigType_NoConfigSection()
	{
		var h = Build();
		h.Controllers.GetByName("Simple").Returns(SimpleController());
		ReturnsModel(h, new PageUpsertViewModel());
		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("select.controller-picker").Change("Simple");
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Not.Contain("Configuration")); // no config type
				Assert.That(cut.Markup, Does.Contain(">Default</option>")); // views still load
			});
		}
	}

	[Test]
	public void ClearController_RemovesConfig()
	{
		var h = Build();
		h.Controllers.GetByName("Blog").Returns(BlogController());
		h.Controllers.CreateDefaultConfiguration("Blog").Returns(new DemoConfig());
		ReturnsModel(h, new PageUpsertViewModel());
		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find("select.controller-picker").Change("Blog");
			Assert.That(cut.Markup, Does.Contain("Configuration"));

			cut.Find("select.controller-picker").Change(""); // GetByName("") -> null -> config cleared
			Assert.That(cut.Markup, Does.Not.Contain("Configuration"));
		}
	}

	[Test]
	public void Save_Valid_SerializesConfig_PersistsViewName_AndNavigates()
	{
		var h = Build();
		h.Controllers.GetByName("Blog").Returns(BlogController());
		ReturnsModel(h, new PageUpsertViewModel { Title = "Blog", Route = "/blog", ControllerName = "Blog", ConfigurationJson = "{\"Heading\":\"Hi\"}" });
		h.Handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
		using (h.Ctx)
		{
			var cut = Render(h, Guid.NewGuid());
			cut.Find("select.view-picker").Change("Wide");
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				h.Handler.Received(1).SaveUpsertAsync(
					Arg.Is<object>(m => ((PageUpsertViewModel)m).ViewName == "Wide" && ((PageUpsertViewModel)m).ConfigurationJson.Contains("Hi")),
					Arg.Any<CancellationToken>());
				Assert.That(h.Nav.Uri, Does.EndWith("/admin/site-pages"));
			});
		}
	}

	[Test]
	public void ViewName_EmptySelection_SetsNull()
	{
		var h = Build();
		h.Controllers.GetByName("Blog").Returns(BlogController());
		ReturnsModel(h, new PageUpsertViewModel { Title = "T", Route = "/r", ControllerName = "Blog", ViewName = "Wide", ConfigurationJson = "{}" });
		h.Handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(new AdminSaveResult(true));
		using (h.Ctx)
		{
			var cut = Render(h, Guid.NewGuid());
			cut.Find("select.view-picker").Change(string.Empty); // -> ViewName null
			cut.Find(".save-content").Click();

			h.Handler.Received(1).SaveUpsertAsync(
				Arg.Is<object>(m => ((PageUpsertViewModel)m).ViewName == null),
				Arg.Any<CancellationToken>());
		}
	}

	[Test]
	public void RestoreMode_LoadsHistoricalPageVersion_AsEdit()
	{
		var h = Build();
		var restoreId = Guid.NewGuid();
		h.Handler.GetRestoreVersionViewModelAsync(restoreId, Arg.Any<CancellationToken>())
			.Returns((object?)new PageUpsertViewModel { Title = "Restored", Route = "/r", ControllerName = string.Empty });
		using (h.Ctx)
		{
			var cut = h.Ctx.Render<AdminPageUpsert>(p => p.Add(c => c.RestoreId, restoreId));

			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Edit Page")); // restore is an edit
				Assert.That(cut.Markup, Does.Contain("value=\"/r\""));
			});
		}
	}

	[Test]
	public void Save_Invalid_ShowsValidationError_AndDoesNotSave()
	{
		var h = Build();
		ReturnsModel(h, new PageUpsertViewModel()); // Route + ControllerName empty -> required fails
		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find(".save-content").Click();

			Assert.Multiple(() =>
			{
				Assert.That(cut.Find(".save-error").TextContent, Is.Not.Empty);
				h.Handler.DidNotReceive().SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
			});
		}
	}

	[Test]
	public void Save_HandlerFailure_ShowsError()
	{
		var h = Build();
		ReturnsModel(h, new PageUpsertViewModel { Title = "Dup", Route = "/dup", ControllerName = "Simple" });
		h.Handler.SaveUpsertAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(new AdminSaveResult(false, "This route is already in use."));
		using (h.Ctx)
		{
			var cut = Render(h);
			cut.Find(".save-content").Click();

			Assert.That(cut.Find(".save-error").TextContent, Does.Contain("already in use"));
		}
	}
}
