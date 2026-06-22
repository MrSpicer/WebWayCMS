using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class AttributeTests
{
	private sealed class Config { }

	[Test]
	public void FormPropertyAttribute_DefaultConstructor_UsesDefaults()
	{
		var attr = new FormPropertyAttribute();

		Assert.Multiple(() =>
		{
			Assert.That(attr.EditorType, Is.EqualTo(EditorType.Text));
			Assert.That(attr.Min, Is.NaN);
			Assert.That(attr.Max, Is.NaN);
			Assert.That(attr.MaxLength, Is.EqualTo(-1));
		});
	}

	[Test]
	public void FormPropertyAttribute_LabelConstructor_SetsLabelAndEditor()
	{
		var attr = new FormPropertyAttribute("My Label", EditorType.Number);

		Assert.Multiple(() =>
		{
			Assert.That(attr.Label, Is.EqualTo("My Label"));
			Assert.That(attr.EditorType, Is.EqualTo(EditorType.Number));
		});
	}

	[Test]
	public void ContentZoneComponentAttribute_DefaultConstructor_UsesDefaults()
	{
		var attr = new ContentZoneComponentAttribute();

		Assert.That(attr.Category, Is.EqualTo("General"));
	}

	[Test]
	public void ContentZoneComponentAttribute_NamedConstructor_SetsValues()
	{
		var attr = new ContentZoneComponentAttribute("Display", typeof(Config));

		Assert.Multiple(() =>
		{
			Assert.That(attr.DisplayName, Is.EqualTo("Display"));
			Assert.That(attr.ConfigurationType, Is.EqualTo(typeof(Config)));
		});
	}

	[Test]
	public void PageControllerAttribute_DefaultConstructor_UsesDefaults()
	{
		var attr = new PageControllerAttribute();

		Assert.That(attr.Category, Is.EqualTo("General"));
	}

	[Test]
	public void PageControllerAttribute_NamedConstructor_SetsValues()
	{
		var attr = new PageControllerAttribute("Display", typeof(Config));

		Assert.Multiple(() =>
		{
			Assert.That(attr.DisplayName, Is.EqualTo("Display"));
			Assert.That(attr.ConfigurationType, Is.EqualTo(typeof(Config)));
		});
	}

	[Test]
	public void CmsChromeAttribute_DefaultOrderIsZero()
		=> Assert.That(new CmsChromeAttribute().Order, Is.EqualTo(0));

	[Test]
	public void CmsPageViewAttribute_Constructors()
	{
		var def = new CmsPageViewAttribute();
		var ctor = new CmsPageViewAttribute("Generic", "Wide");

		Assert.Multiple(() =>
		{
			Assert.That(def.ForController, Is.Empty);
			Assert.That(def.Name, Is.Empty);
			Assert.That(ctor.ForController, Is.EqualTo("Generic"));
			Assert.That(ctor.Name, Is.EqualTo("Wide"));
		});
	}

	[Test]
	public void ContentZoneViewAttribute_Constructors()
	{
		var def = new ContentZoneViewAttribute();
		var ctor = new ContentZoneViewAttribute("ContentBlock", "Card");

		Assert.Multiple(() =>
		{
			Assert.That(def.ForComponent, Is.Empty);
			Assert.That(def.Name, Is.Empty);
			Assert.That(ctor.ForComponent, Is.EqualTo("ContentBlock"));
			Assert.That(ctor.Name, Is.EqualTo("Card"));
		});
	}

	private sealed class FooWidget { }
	private sealed class BarViewComponent { }
	private sealed class Plain { }

	[Test]
	public void ContentZoneComponentNaming_NullArgs_Throw()
	{
		Assert.Multiple(() =>
		{
			Assert.That(() => ContentZoneComponentNaming.ResolveName(null!, new ContentZoneComponentAttribute()),
				Throws.ArgumentNullException);
			Assert.That(() => ContentZoneComponentNaming.ResolveName(typeof(FooWidget), null!),
				Throws.ArgumentNullException);
		});
	}

	[Test]
	public void ContentZoneComponentNaming_ResolvesExplicitNameSuffixesAndPlain()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ContentZoneComponentNaming.ResolveName(typeof(FooWidget), new ContentZoneComponentAttribute { Name = "Explicit" }),
				Is.EqualTo("Explicit"));
			Assert.That(ContentZoneComponentNaming.ResolveName(typeof(FooWidget), new ContentZoneComponentAttribute()),
				Is.EqualTo("Foo"));
			Assert.That(ContentZoneComponentNaming.ResolveName(typeof(BarViewComponent), new ContentZoneComponentAttribute()),
				Is.EqualTo("Bar"));
			Assert.That(ContentZoneComponentNaming.ResolveName(typeof(Plain), new ContentZoneComponentAttribute()),
				Is.EqualTo("Plain"));
		});
	}
}
