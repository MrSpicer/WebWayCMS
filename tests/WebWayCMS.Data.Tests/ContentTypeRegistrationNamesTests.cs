using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.ContentTypes;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class ContentTypeRegistrationNamesTests
{
    private static Dictionary<string, ContentTypeRegistrationDTO> Names() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["articlelists"] = new() { ContentTypeKey = "articlelists", DisplayName = "Reading List", PluralDisplayName = "Reading Lists" },
        ["blank"] = new() { ContentTypeKey = "blank", DisplayName = " ", PluralDisplayName = "" },
    };

    [Test]
    public void DisplayNameOr_UsesTheRegistration_ElseTheFallback()
    {
        var names = Names();

        Assert.Multiple(() =>
        {
            Assert.That(names.DisplayNameOr("articlelists", "Article List"), Is.EqualTo("Reading List"));
            Assert.That(names.DisplayNameOr("blank", "Blank"), Is.EqualTo("Blank"));
            Assert.That(names.DisplayNameOr("unknown", "Unknown"), Is.EqualTo("Unknown"));
            Assert.That(names.DisplayNameOr(null, "None"), Is.EqualTo("None"));
        });
    }

    [Test]
    public void PluralDisplayNameOr_UsesTheRegistration_ElseTheFallback()
    {
        var names = Names();

        Assert.Multiple(() =>
        {
            Assert.That(names.PluralDisplayNameOr("articlelists", "Article Lists"), Is.EqualTo("Reading Lists"));
            Assert.That(names.PluralDisplayNameOr("blank", "Blanks"), Is.EqualTo("Blanks"));
            Assert.That(names.PluralDisplayNameOr("unknown", "Unknowns"), Is.EqualTo("Unknowns"));
        });
    }

    [Test]
    public void NullDictionary_Throws()
        => Assert.That(() => ((IReadOnlyDictionary<string, ContentTypeRegistrationDTO>)null!).DisplayNameOr("x", "y"),
            Throws.ArgumentNullException);
}
