using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using NSubstitute;

using NUnit.Framework;

using WebWayCMS.Services;

namespace WebWayCMS.Identity.Tests;

[TestFixture]
public class UserServiceTests
{
	private static ClaimsPrincipal AuthenticatedPrincipal(params string[] roles)
	{
		var claims = new List<Claim>(roles.Select(r => new Claim(ClaimTypes.Role, r)));
		// A non-null authentication type makes Identity.IsAuthenticated == true.
		var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
		return new ClaimsPrincipal(identity);
	}

	private static UserService CreateService(HttpContext? context)
	{
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(context);
		return new UserService(accessor);
	}

	private static HttpContext ContextWith(ClaimsPrincipal user)
	{
		var context = new DefaultHttpContext { User = user };
		return context;
	}

	[Test]
	public void Constructor_NullAccessor_Throws()
	{
		Assert.That(() => new UserService(null!), Throws.ArgumentNullException);
	}

	[Test]
	public void IsUserAuthor_NullHttpContext_ReturnsFalse()
	{
		var service = CreateService(null);

		Assert.That(service.IsUserAuthor, Is.False);
	}

	[Test]
	public void IsUserAuthor_NullUser_ReturnsFalse()
	{
		var context = Substitute.For<HttpContext>();
		context.User.Returns((ClaimsPrincipal)null!);
		var service = CreateService(context);

		Assert.That(service.IsUserAuthor, Is.False);
	}

	[Test]
	public void IsUserAuthor_NullIdentity_ReturnsFalse()
	{
		var user = new ClaimsPrincipal();
		var service = CreateService(ContextWith(user));

		Assert.That(service.IsUserAuthor, Is.False);
	}

	[Test]
	public void IsUserAuthor_NotAuthenticated_ReturnsFalse()
	{
		// ClaimsIdentity with no authentication type is not authenticated.
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var service = CreateService(ContextWith(user));

		Assert.That(service.IsUserAuthor, Is.False);
	}

	[Test]
	public void IsUserAuthor_AuthenticatedAdmin_ReturnsTrue()
	{
		var service = CreateService(ContextWith(AuthenticatedPrincipal("Admin")));

		Assert.That(service.IsUserAuthor, Is.True);
	}

	[Test]
	public void IsUserAuthor_AuthenticatedEditor_ReturnsTrue()
	{
		var service = CreateService(ContextWith(AuthenticatedPrincipal("Editor")));

		Assert.That(service.IsUserAuthor, Is.True);
	}

	[Test]
	public void IsUserAuthor_AuthenticatedWithoutRoles_ReturnsFalse()
	{
		var service = CreateService(ContextWith(AuthenticatedPrincipal()));

		Assert.That(service.IsUserAuthor, Is.False);
	}

	[Test]
	public void IsUserAdmin_NullHttpContext_ReturnsFalse()
	{
		var service = CreateService(null);

		Assert.That(service.IsUserAdmin, Is.False);
	}

	[Test]
	public void IsUserAdmin_AuthenticatedAdmin_ReturnsTrue()
	{
		var service = CreateService(ContextWith(AuthenticatedPrincipal("Admin")));

		Assert.That(service.IsUserAdmin, Is.True);
	}

	[Test]
	public void IsUserAdmin_AuthenticatedEditor_ReturnsFalse()
	{
		var service = CreateService(ContextWith(AuthenticatedPrincipal("Editor")));

		Assert.That(service.IsUserAdmin, Is.False);
	}

	[Test]
	public void IsUserAdmin_NotAuthenticated_ReturnsFalse()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var service = CreateService(ContextWith(user));

		Assert.That(service.IsUserAdmin, Is.False);
	}

	[Test]
	public void IsUserAdmin_NullUser_ReturnsFalse()
	{
		var context = Substitute.For<HttpContext>();
		context.User.Returns((ClaimsPrincipal)null!);
		var service = CreateService(context);

		Assert.That(service.IsUserAdmin, Is.False);
	}

	[Test]
	public void IsUserAdmin_NullIdentity_ReturnsFalse()
	{
		var user = new ClaimsPrincipal();
		var service = CreateService(ContextWith(user));

		Assert.That(service.IsUserAdmin, Is.False);
	}
}
