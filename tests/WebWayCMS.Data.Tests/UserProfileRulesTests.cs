using NUnit.Framework;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class UserProfileRulesTests
{
    [Test]
    public void AdminOrEditor_IsBothRoles_AsAFreshArrayEachTime()
    {
        var first = UserProfileRules.AdminOrEditor;
        first[0] = "changed";

        Assert.That(UserProfileRules.AdminOrEditor, Is.EqualTo(new[] { UserProfileRules.AdminRole, UserProfileRules.EditorRole }));
    }

    [Test]
    public void BuiltInRoles_AreAdminEditorAndTheDefaultRole()
        => Assert.That(UserProfileRules.BuiltInRoles,
            Is.EqualTo(new[] { UserProfileRules.AdminRole, UserProfileRules.EditorRole, UserProfileRules.DefaultRole }));
}
