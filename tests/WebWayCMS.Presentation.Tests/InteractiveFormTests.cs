using Bunit;

using NUnit.Framework;

using WebWayCMS.Attributes;
using WebWayCMS.Forms;
using WebWayCMS.Presentation.Components.Admin;
using WebWayCMS.Presentation.Rendering;

namespace WebWayCMS.Presentation.Tests;

[TestFixture]
public class FormValueConverterTests
{
	[Test]
	public void ToInput_FormatsByEditorType()
	{
		Assert.Multiple(() =>
		{
			Assert.That(FormValueConverter.ToInput(null, EditorType.Text), Is.EqualTo(string.Empty));
			Assert.That(FormValueConverter.ToInput("hi", EditorType.Text), Is.EqualTo("hi"));
			Assert.That(FormValueConverter.ToInput(5, EditorType.Number), Is.EqualTo("5"));
			Assert.That(FormValueConverter.ToInput(new DateTime(2024, 1, 15, 9, 30, 0), EditorType.DateTime), Is.EqualTo("2024-01-15T09:30"));
			Assert.That(FormValueConverter.ToInput(default(DateTime), EditorType.DateTime), Is.EqualTo(string.Empty));
			Assert.That(FormValueConverter.ToInput("s", EditorType.DateTime), Is.EqualTo("s"));
			Assert.That(FormValueConverter.ToInput(new DateTime(2024, 1, 15), EditorType.Date), Is.EqualTo("2024-01-15"));
			Assert.That(FormValueConverter.ToInput(default(DateTime), EditorType.Date), Is.EqualTo(string.Empty));
			Assert.That(FormValueConverter.ToInput("s", EditorType.Date), Is.EqualTo("s"));
			Assert.That(FormValueConverter.ToInput(Guid.Empty, EditorType.Guid), Is.EqualTo(string.Empty));
			var g = Guid.NewGuid();
			Assert.That(FormValueConverter.ToInput(g, EditorType.Guid), Is.EqualTo(g.ToString()));
			Assert.That(FormValueConverter.ToInput("s", EditorType.Guid), Is.EqualTo("s"));
		});
	}

	[Test]
	public void FromInput_EmptyAndStrings()
	{
		Assert.Multiple(() =>
		{
			Assert.That(FormValueConverter.FromInput("", typeof(string)), Is.Null);
			Assert.That(FormValueConverter.FromInput("hi", typeof(string)), Is.EqualTo("hi"));
			Assert.That(FormValueConverter.FromInput("", typeof(int)), Is.EqualTo(0));
			Assert.That(FormValueConverter.FromInput("", typeof(int?)), Is.Null);
		});
	}

	[Test]
	public void FromInput_TypedParsingAndFallbacks()
	{
		var g = Guid.NewGuid();
		Assert.Multiple(() =>
		{
			Assert.That(FormValueConverter.FromInput("true", typeof(bool)), Is.EqualTo(true));
			Assert.That(FormValueConverter.FromInput("false", typeof(bool)), Is.EqualTo(false));
			Assert.That(FormValueConverter.FromInput("5", typeof(int)), Is.EqualTo(5));
			Assert.That(FormValueConverter.FromInput("x", typeof(int)), Is.EqualTo(0));
			Assert.That(FormValueConverter.FromInput("x", typeof(int?)), Is.Null);
			Assert.That(FormValueConverter.FromInput("1.5", typeof(double)), Is.EqualTo(1.5));
			Assert.That(FormValueConverter.FromInput("x", typeof(double)), Is.EqualTo(0d));
			Assert.That(FormValueConverter.FromInput(g.ToString(), typeof(Guid)), Is.EqualTo(g));
			Assert.That(FormValueConverter.FromInput("x", typeof(Guid?)), Is.Null);
			Assert.That(FormValueConverter.FromInput("2024-01-15", typeof(DateTime)), Is.EqualTo(new DateTime(2024, 1, 15)));
			Assert.That(FormValueConverter.FromInput("x", typeof(DateTime)), Is.EqualTo(default(DateTime)));
			Assert.That(FormValueConverter.FromInput("foo", typeof(long)), Is.EqualTo("foo")); // unhandled type -> raw passthrough
		});
	}
}

[TestFixture]
public class InteractiveFormFieldsTests
{
	private sealed class FormModel
	{
		public string? Text { get; set; }
		public string? Area { get; set; }
		public string? Rich { get; set; }
		public bool Flag { get; set; }
		public string? Choice { get; set; }
		public string? HiddenVal { get; set; }
	}

	private static List<FormPropertyInfo> Props() => new()
	{
		new() { Name = "Text", Label = "Text", EditorType = EditorType.Text, HelpText = "help text" },
		new() { Name = "Area", Label = "Area", EditorType = EditorType.TextArea },
		new() { Name = "Rich", Label = "Rich", EditorType = EditorType.RichText },
		new() { Name = "Flag", Label = "Flag", EditorType = EditorType.Checkbox },
		new() { Name = "MissingFlag", Label = "MissingFlag", EditorType = EditorType.Checkbox },
		new() { Name = "Choice", Label = "Choice", EditorType = EditorType.Dropdown, DropdownOptions = new() { ["a"] = "A", ["b"] = "B" } },
		new() { Name = "HiddenVal", Label = "Hidden", EditorType = EditorType.Hidden },
		new() { Name = "Missing", Label = "Missing", EditorType = EditorType.Text },
	};

	private static (BunitContext Ctx, IRenderedComponent<InteractiveFormFields> Cut, FormModel Model) Render(FormModel? model = null)
	{
		var ctx = new BunitContext();
		var m = model ?? new FormModel();
		var cut = ctx.Render<InteractiveFormFields>(p => p
			.Add(c => c.Model, m)
			.Add(c => c.Properties, Props()));
		return (ctx, cut, m);
	}

	[Test]
	public void RendersEachEditorType_AndHelpText()
	{
		var (ctx, cut, _) = Render(new FormModel { Choice = "a", Flag = true });
		using (ctx)
		{
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("type=\"hidden\""));
				Assert.That(cut.Markup, Does.Contain("type=\"checkbox\""));
				Assert.That(cut.Markup, Does.Contain("<textarea"));
				Assert.That(cut.Markup, Does.Contain("<select"));
				Assert.That(cut.Markup, Does.Contain(">A</option>"));
				Assert.That(cut.Markup, Does.Contain("help text"));
				Assert.That(cut.Markup, Does.Contain("selected")); // Choice="a" -> option a selected
			});
		}
	}

	[Test]
	public void EditingFields_UpdatesBoundModel()
	{
		var (ctx, cut, model) = Render();
		using (ctx)
		{
			cut.Find("input[data-prop=\"Text\"]").Change("hello");
			cut.Find("textarea[data-prop=\"Area\"]").Change("body");
			cut.Find("input[data-prop=\"Flag\"]").Change(true);
			cut.Find("select[data-prop=\"Choice\"]").Change("b");
			cut.Find("input[data-prop=\"HiddenVal\"]").Change("hv");

			Assert.Multiple(() =>
			{
				Assert.That(model.Text, Is.EqualTo("hello"));
				Assert.That(model.Area, Is.EqualTo("body"));
				Assert.That(model.Flag, Is.True);
				Assert.That(model.Choice, Is.EqualTo("b"));
				Assert.That(model.HiddenVal, Is.EqualTo("hv"));
			});
		}
	}

	[Test]
	public void Field_WithProvidedOptions_RendersSelect_AndBinds()
	{
		var ctx = new BunitContext();
		var model = new FormModel { Text = "x" }; // current value -> option "x" selected
		var options = new Dictionary<string, IReadOnlyList<FormOption>>
		{
			["Text"] = new[] { new FormOption("x", "X-Label"), new FormOption("y", "Y-Label") },
		};

		var cut = ctx.Render<InteractiveFormFields>(p => p
			.Add(c => c.Model, model)
			.Add(c => c.Properties, Props())
			.Add(c => c.Options, options));

		using (ctx)
		{
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain(">X-Label</option>"));
				Assert.That(cut.Markup, Does.Contain(">Y-Label</option>"));
			});

			cut.Find("select[data-prop=\"Text\"]").Change("y");
			Assert.That(model.Text, Is.EqualTo("y"));
		}
	}

	[Test]
	public void Checkbox_Toggles_TrueAndFalse()
	{
		var (ctx, cut, model) = Render();
		using (ctx)
		{
			cut.Find("input[data-prop=\"Flag\"]").Change(true);
			Assert.That(model.Flag, Is.True);

			cut.Find("input[data-prop=\"Flag\"]").Change(false);
			Assert.That(model.Flag, Is.False);
		}
	}

	[Test]
	public void EditingUnmappedProperties_IsIgnored()
	{
		var (ctx, cut, model) = Render();
		using (ctx)
		{
			// "Missing"/"MissingFlag" have no matching model property -> setters must be no-ops, not throw.
			Assert.That(() =>
			{
				cut.Find("input[data-prop=\"Missing\"]").Change("x");
				cut.Find("input[data-prop=\"MissingFlag\"]").Change(true);
			}, Throws.Nothing);
			Assert.That(model.Flag, Is.False);
		}
	}

	[Test]
	public void RendersInputTypes_RequiredMarkers_AndGroupHeaders()
	{
		var ctx = new BunitContext();
		var props = new List<FormPropertyInfo>
		{
			new() { Name = "When", Label = "When", EditorType = EditorType.DateTime },
			new() { Name = "Day", Label = "Day", EditorType = EditorType.Date },
			new() { Name = "Count", Label = "Count", EditorType = EditorType.Number },
			new() { Name = "Link", Label = "Link", EditorType = EditorType.Url },
			new() { Name = "Mail", Label = "Mail", EditorType = EditorType.Email },
			new() { Name = "Shade", Label = "Shade", EditorType = EditorType.Color },
			new() { Name = "Title", Label = "Title", EditorType = EditorType.Text, IsRequired = true },
			new() { Name = "Pub", Label = "Pub", EditorType = EditorType.Checkbox, IsRequired = true, Group = "Publishing" },
			new() { Name = "Stat", Label = "Stat", EditorType = EditorType.Text, Group = "Publishing" }, // same group -> no second header
			new() { Name = "Arch", Label = "Arch", EditorType = EditorType.Text, Group = "Status" },
		};

		// Display tolerates properties absent from the model, so a bare object suffices here.
		var cut = ctx.Render<InteractiveFormFields>(p => p
			.Add(c => c.Model, new object())
			.Add(c => c.Properties, props));

		using (ctx)
		{
			Assert.Multiple(() =>
			{
				Assert.That(cut.Markup, Does.Contain("type=\"datetime-local\""));
				Assert.That(cut.Markup, Does.Contain("type=\"date\""));
				Assert.That(cut.Markup, Does.Contain("type=\"number\""));
				Assert.That(cut.Markup, Does.Contain("type=\"url\""));
				Assert.That(cut.Markup, Does.Contain("type=\"email\""));
				Assert.That(cut.Markup, Does.Contain("type=\"color\""));
				Assert.That(cut.Markup, Does.Contain("<span class=\"has-text-danger\">*</span>"));
				Assert.That(cut.Markup, Does.Contain(">Publishing</h3>"));
				Assert.That(cut.Markup, Does.Contain(">Status</h3>"));
				Assert.That(System.Text.RegularExpressions.Regex.Matches(cut.Markup, ">Publishing</h3>"), Has.Count.EqualTo(1));
			});
		}
	}
}
