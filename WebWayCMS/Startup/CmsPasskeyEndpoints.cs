using System.Diagnostics.CodeAnalysis;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace WebWayCMS.Startup;

/// <summary>
/// Minimal-API endpoints backing the passkey (WebAuthn) flows. These are hand-written because
/// WebWayCMS is an MVC/Razor Pages app, not the Blazor Web App template that ships the default
/// passkey scaffolding. They map the framework-agnostic <see cref="SignInManager{TUser}"/> /
/// <see cref="UserManager{TUser}"/> passkey APIs to the <c>passkeys.js</c> client helpers.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class CmsPasskeyEndpoints
{
    internal static IEndpointRouteBuilder MapPasskeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/Identity/Account/PasskeyCreationOptions",
            async (HttpContext httpContext, SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager) =>
            {
                var user = await userManager.GetUserAsync(httpContext.User);
                if (user is null)
                    return Results.Unauthorized();

                var userName = await userManager.GetUserNameAsync(user) ?? string.Empty;
                var userEntity = new PasskeyUserEntity
                {
                    Id = await userManager.GetUserIdAsync(user),
                    Name = userName,
                    DisplayName = userName,
                };

                var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(userEntity);
                return Results.Content(optionsJson, "application/json");
            }).RequireAuthorization();

        endpoints.MapPost("/Identity/Account/PasskeyRegistration",
            async (HttpContext httpContext, SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager) =>
            {
                var user = await userManager.GetUserAsync(httpContext.User);
                if (user is null)
                    return Results.Unauthorized();

                var credentialJson = await ReadBodyAsync(httpContext.Request);
                var attestation = await signInManager.PerformPasskeyAttestationAsync(credentialJson);
                if (!attestation.Succeeded || attestation.Passkey is null)
                {
                    var message = attestation.Failure?.Message ?? "Passkey attestation failed.";
                    return Results.Problem(title: "Passkey attestation failed.", detail: message, statusCode: StatusCodes.Status400BadRequest);
                }

                if (string.IsNullOrEmpty(attestation.Passkey.Name))
                {
                    attestation.Passkey.Name = await userManager.GetUserNameAsync(user);
                }

                var addResult = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
                if (!addResult.Succeeded)
                    return Results.Problem(title: "Failed to save passkey.", statusCode: StatusCodes.Status400BadRequest);

                return Results.Ok();
            }).RequireAuthorization();

        endpoints.MapPost("/Identity/Account/PasskeyRequestOptions",
            async (SignInManager<IdentityUser> signInManager) =>
            {
                var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(null!);
                return Results.Content(optionsJson, "application/json");
            });

        endpoints.MapPost("/Identity/Account/PasskeyAssertion",
            async (HttpContext httpContext, SignInManager<IdentityUser> signInManager) =>
            {
                var credentialJson = await ReadBodyAsync(httpContext.Request);
                var result = await signInManager.PasskeySignInAsync(credentialJson);
                if (result.Succeeded)
                    return Results.Ok();

                if (result.RequiresTwoFactor)
                    return Results.Ok(new { requiresTwoFactor = true });

                if (result.IsLockedOut)
                    return Results.Problem(title: "Account locked out.", statusCode: StatusCodes.Status423Locked);

                if (result.IsNotAllowed)
                    return Results.Problem(title: "Confirm your email address before signing in.", statusCode: StatusCodes.Status400BadRequest);

                return Results.Problem(title: "Passkey sign-in failed.", statusCode: StatusCodes.Status400BadRequest);
            });

        return endpoints;
    }

    private const int MaxPasskeyBodyBytes = 64 * 1024;

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxPasskeyBodyBytes)
            {
                throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
