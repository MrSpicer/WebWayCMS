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
}
