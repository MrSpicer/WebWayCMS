using Microsoft.AspNetCore.Routing;

using NUnit.Framework;

namespace WebWayCMS.Routing.Tests;

[TestFixture]
public class NotReservedConstraintTests
{
	private static bool Match(RouteValueDictionary values)
	{
		var constraint = new NotReservedConstraint();
		return constraint.Match(null, null, "parentKey", values, RouteDirection.IncomingRequest);
	}

	[TestCase("edit")]
	[TestCase("delete")]
	[TestCase("create")]
	[TestCase("registry")]
	[TestCase("api")]
	[TestCase("reorder")]
	[TestCase("versions")]
	[TestCase("EDIT")]
	public void Match_ReservedWord_ReturnsFalse(string reserved)
	{
		Assert.That(Match(new RouteValueDictionary { ["parentKey"] = reserved }), Is.False);
	}

	[Test]
	public void Match_NonReservedWord_ReturnsTrue()
	{
		Assert.That(Match(new RouteValueDictionary { ["parentKey"] = "about" }), Is.True);
	}

	[Test]
	public void Match_MissingKey_ReturnsFalse()
	{
		Assert.That(Match(new RouteValueDictionary()), Is.False);
	}

	[Test]
	public void Match_NonStringValue_ReturnsFalse()
	{
		Assert.That(Match(new RouteValueDictionary { ["parentKey"] = 42 }), Is.False);
	}
}
