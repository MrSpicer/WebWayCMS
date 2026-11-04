using NUnit.Framework;

using WebWayCMS.Attributes;

namespace WebWayCMS.Forms.Tests;

[TestFixture]
public class CMSFormComponentAttributeTests
{
    [Test]
    public void Constructor_WithName_SetsName()
    {
        var attr = new CMSFormComponentAttribute("RichText");

        Assert.That(attr.Name, Is.EqualTo("RichText"));
    }

    [Test]
    public void Constructor_WithName_DefaultsOtherProperties()
    {
        var attr = new CMSFormComponentAttribute("Text");

        Assert.Multiple(() =>
        {
            Assert.That(attr.DataTypes, Is.Empty);
            Assert.That(attr.IsDefaultForType, Is.False);
            Assert.That(attr.EditorType, Is.Null);
            Assert.That(attr.DisplayName, Is.Empty);
            Assert.That(attr.Description, Is.Empty);
            Assert.That(attr.Category, Is.EqualTo("General"));
            Assert.That(attr.IconClass, Is.Empty);
            Assert.That(attr.Order, Is.EqualTo(0));
        });
    }

    [Test]
    public void Constructor_Parameterless_SetsNameToEmpty()
    {
        var attr = new CMSFormComponentAttribute();

        Assert.That(attr.Name, Is.Empty);
    }

    [Test]
    public void Constructor_Parameterless_DefaultsOtherProperties()
    {
        var attr = new CMSFormComponentAttribute();

        Assert.Multiple(() =>
        {
            Assert.That(attr.DataTypes, Is.Empty);
            Assert.That(attr.IsDefaultForType, Is.False);
            Assert.That(attr.EditorType, Is.Null);
        });
    }

    [Test]
    public void SettableProperties_CanBeSet()
    {
        var attr = new CMSFormComponentAttribute("Custom")
        {
            DataTypes = new[] { typeof(string), typeof(int) },
            IsDefaultForType = true,
            EditorType = EditorType.Text,
            DisplayName = "My Component",
            Description = "A custom component",
            Category = "Special",
            IconClass = "fa-star",
            Order = 5
        };

        Assert.Multiple(() =>
        {
            Assert.That(attr.Name, Is.EqualTo("Custom"));
            Assert.That(attr.DataTypes, Is.EquivalentTo(new[] { typeof(string), typeof(int) }));
            Assert.That(attr.IsDefaultForType, Is.True);
            Assert.That(attr.EditorType, Is.EqualTo(EditorType.Text));
            Assert.That(attr.DisplayName, Is.EqualTo("My Component"));
            Assert.That(attr.Description, Is.EqualTo("A custom component"));
            Assert.That(attr.Category, Is.EqualTo("Special"));
            Assert.That(attr.IconClass, Is.EqualTo("fa-star"));
            Assert.That(attr.Order, Is.EqualTo(5));
        });
    }
}
