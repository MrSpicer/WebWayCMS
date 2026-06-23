using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Forms;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class ContentZoneItemFormTests
{
	public class DemoConfig
	{
		[FormProperty("Name", EditorType.Text)]
		public string? Name { get; set; }
	}

	private static IContentZoneComponentRegistry Registry()
	{
		var demo = new ContentZoneComponentInfo
		{
			Name = "Demo",
			DisplayName = "Demo",
			Category = "General",
			ConfigurationType = typeof(DemoConfig),
			Properties = FormPropertyBuilder.BuildPropertyInfos(typeof(DemoConfig)),
		};
		var bare = new ContentZoneComponentInfo { Name = "Bare", DisplayName = "Bare", Category = "General", ConfigurationType = null };

		var reg = Substitute.For<IContentZoneComponentRegistry>();
		reg.GetComponentsByCategory().Returns(new Dictionary<string, IReadOnlyList<ContentZoneComponentInfo>>
		{
			["General"] = new[] { demo, bare, new ContentZoneComponentInfo { Name = "Ghost", DisplayName = "Ghost" } },
		});
		reg.GetByName("Demo").Returns(demo);
		reg.GetByName("Bare").Returns(bare);
		reg.GetByName("Ghost").Returns((ContentZoneComponentInfo?)null);
		reg.CreateDefaultConfiguration("Demo").Returns(_ => new DemoConfig());
		reg.CreateDefaultConfiguration("Bare").Returns((object?)null);
		return reg;
	}

	private static IFormOptionsProvider EmptyOptions()
	{
		var p = Substitute.For<IFormOptionsProvider>();
		p.GetOptionsAsync(Arg.Any<FormPropertyInfo>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(Array.Empty<FormOption>()));
		return p;
	}

	private static IContentZoneViewRegistry EmptyViews()
	{
		var v = Substitute.For<IContentZoneViewRegistry>();
		v.GetComponentViews(Arg.Any<string>()).Returns(Array.Empty<string>());
		return v;
	}

	private static (BunitContext Ctx, IRenderedComponent<ContentZoneItemForm> Cut, List<ContentZoneItemFormResult> Saved, List<bool> Cancelled) Render(
		string? editComponent = null, string? editJson = null, IFormOptionsProvider? options = null,
		IContentZoneViewRegistry? views = null, string? editViewName = null)
	{
		var ctx = new BunitContext();
		ctx.Services.AddSingleton(Registry());
		ctx.Services.AddSingleton(options ?? EmptyOptions());
		ctx.Services.AddSingleton(views ?? EmptyViews());
		var saved = new List<ContentZoneItemFormResult>();
		var cancelled = new List<bool>();
		var cut = ctx.Render<ContentZoneItemForm>(p => p
			.Add(c => c.EditComponentName, editComponent)
			.Add(c => c.EditViewName, editViewName)
			.Add(c => c.EditJson, editJson)
			.Add(c => c.OnSaved, (ContentZoneItemFormResult r) => saved.Add(r))
			.Add(c => c.OnCancel, () => cancelled.Add(true)));
		return (ctx, cut, saved, cancelled);
	}

	[Test]
	public void AddMode_InitiallyNoFormAndSaveNoOpsWhenUnselected()
	{
		var (ctx, cut, saved, _) = Render();
		using (ctx)
		{
			Assert.That(cut.Markup, Does.Contain("Add Widget"));
			cut.Find(".save-form").Click(); // disabled / no component -> no save
			Assert.That(saved, Is.Empty);
		}
	}

	[Test]
	public void AddMode_SelectConfiguredComponent_ShowsFormAndSaves()
	{
		var (ctx, cut, saved, _) = Render();
		using (ctx)
		{
			cut.Find(".component-selector").Change("Demo");
			Assert.That(cut.Markup, Does.Contain("Name")); // the DemoConfig field

			cut.Find("input[data-prop=\"Name\"]").Change("hello");
			cut.Find(".save-form").Click();

			Assert.That(saved, Has.Count.EqualTo(1));
			Assert.That(saved[0].ComponentName, Is.EqualTo("Demo"));
			Assert.That(saved[0].Json, Does.Contain("hello"));
		}
	}

	[Test]
	public void AddMode_SelectEmpty_HidesForm()
	{
		var (ctx, cut, _, _) = Render();
		using (ctx)
		{
			cut.Find(".component-selector").Change("Demo");
			Assert.That(cut.Markup, Does.Contain("data-prop=\"Name\""));
			cut.Find(".component-selector").Change("");
			Assert.That(cut.Markup, Does.Not.Contain("data-prop=\"Name\""));
		}
	}

	[Test]
	public void AddMode_SelectUnknownComponent_NoForm()
	{
		var (ctx, cut, _, _) = Render();
		using (ctx)
		{
			cut.Find(".component-selector").Change("Ghost"); // GetByName -> null
			Assert.That(cut.Markup, Does.Not.Contain("data-prop"));
		}
	}

	[Test]
	public void AddMode_NoConfigComponent_SavesEmptyJson()
	{
		var (ctx, cut, saved, _) = Render();
		using (ctx)
		{
			cut.Find(".component-selector").Change("Bare"); // ConfigurationType null, default null
			cut.Find(".save-form").Click();

			Assert.That(saved, Has.Count.EqualTo(1));
			Assert.That(saved[0], Is.EqualTo(new ContentZoneItemFormResult("Bare", "", "{}")));
		}
	}

	private static IContentZoneViewRegistry ViewsFor(string component, params string[] names)
	{
		var v = Substitute.For<IContentZoneViewRegistry>();
		v.GetComponentViews(Arg.Any<string>()).Returns(Array.Empty<string>());
		v.GetComponentViews(component).Returns(names);
		return v;
	}

	[Test]
	public void AddMode_WithViews_ShowsDropdown_AndSavesSelectedView()
	{
		var (ctx, cut, saved, _) = Render(views: ViewsFor("Demo", "Card", "Banner"));
		using (ctx)
		{
			cut.Find(".component-selector").Change("Demo");
			Assert.That(cut.Markup, Does.Contain("view-selector"));
			Assert.That(cut.Markup, Does.Contain(">Card</option>"));

			cut.Find(".view-selector").Change("Card");
			cut.Find(".save-form").Click();

			Assert.That(saved[0].ViewName, Is.EqualTo("Card"));
		}
	}

	[Test]
	public void AddMode_NoViews_HidesDropdown()
	{
		var (ctx, cut, _, _) = Render();
		using (ctx)
		{
			cut.Find(".component-selector").Change("Demo");
			Assert.That(cut.Markup, Does.Not.Contain("view-selector"));
		}
	}

	[Test]
	public void AddMode_SelectThenClearView_SavesEmptyView()
	{
		var (ctx, cut, saved, _) = Render(views: ViewsFor("Demo", "Card"));
		using (ctx)
		{
			cut.Find(".component-selector").Change("Demo");
			cut.Find(".view-selector").Change("Card");
			cut.Find(".view-selector").Change(""); // back to default
			cut.Find(".save-form").Click();

			Assert.That(saved[0].ViewName, Is.Empty);
		}
	}

	[Test]
	public void EditMode_PrefillsSelectedView_AndSaves()
	{
		var (ctx, cut, saved, _) = Render("Demo", "{}", views: ViewsFor("Demo", "Card", "Banner"), editViewName: "Banner");
		using (ctx)
		{
			Assert.That(cut.Markup, Does.Contain("value=\"Banner\" selected"));
			cut.Find(".save-form").Click();
			Assert.That(saved[0].ViewName, Is.EqualTo("Banner"));
		}
	}

	[Test]
	public void EditMode_PrefillsAndSaves()
	{
		var (ctx, cut, saved, _) = Render("Demo", "{\"Name\":\"existing\"}");
		using (ctx)
		{
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("Edit Widget"));
				Assert.That(cut.Markup, Does.Contain("existing"));
				Assert.That(cut.Find(".component-selector").HasAttribute("disabled"), Is.True);
			});

			cut.Find(".save-form").Click();
			Assert.That(saved[0].ComponentName, Is.EqualTo("Demo"));
		}
	}

	[Test]
	public void EditMode_EmptyJson_UsesDefaultConfig()
	{
		var (ctx, cut, _, _) = Render("Demo", "{}");
		using (ctx)
		{
			Assert.That(cut.Markup, Does.Contain("data-prop=\"Name\""));
		}
	}

	[Test]
	public void SelectComponent_WithFieldOptions_RendersSelect()
	{
		var provider = Substitute.For<IFormOptionsProvider>();
		provider.GetOptionsAsync(Arg.Any<FormPropertyInfo>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(Array.Empty<FormOption>()));
		provider.GetOptionsAsync(Arg.Is<FormPropertyInfo>(p => p.Name == "Name"), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<FormOption>>(new[] { new FormOption("v1", "View One") }));

		var (ctx, cut, _, _) = Render(options: provider);
		using (ctx)
		{
			cut.Find(".component-selector").Change("Demo");
			Assert.That(cut.Markup, Does.Contain(">View One</option>"));
		}
	}

	[Test]
	public void Cancel_InvokesCallback()
	{
		var (ctx, cut, _, cancelled) = Render();
		using (ctx)
		{
			cut.Find(".cancel-form").Click();
			Assert.That(cancelled, Has.Count.EqualTo(1));
		}
	}
}
