using Microsoft.AspNetCore.Http;

namespace WebWayCMS.Services;

public class UserService
{
	private readonly IHttpContextAccessor _httpContextAccessor;

	public UserService(IHttpContextAccessor httpContextAccessor)
	{
		_httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
	}

	public bool IsUserAuthor =>
		_httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true &&
		(_httpContextAccessor.HttpContext.User.IsInRole("Admin") || _httpContextAccessor.HttpContext.User.IsInRole("Editor"));

	public bool IsUserAdmin => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true &&
							   _httpContextAccessor.HttpContext.User.IsInRole("Admin");
}