# Area 8: Identity & Authentication

**Namespaces:**
- `WebWayCMS.Data.DbContexts` — `ApplicationDbContext`
- `WebWayCMS.Services` — `UserService`, `DevEmailSender`
- `WebWayCMS.Presentation.Components.Account` — Blazor Identity components (Login, Register, Manage/*, …) + helpers (`IdentityRedirectManager`, `IdentityUserAccessor`, `IdentityRevalidatingAuthenticationStateProvider`, `IdentityEmailSender`)

**Depends on:** ASP.NET Identity, EF Core (`ApplicationDbContext`)
**Consumed by:** All admin pages/controllers (`[Authorize(Roles = "Admin")]`), `UserService` consumed in components and admin write checks, `CMSExtensions` for seeding

---

## 1. Role Model

Three roles are seeded at startup:

| Role | Capabilities |
|------|-------------|
| `Admin` | Full access to all admin routes; write access to all content types; access to destructive operations (delete, version delete) |
| `Editor` | Read access to admin UI; write access to content types that specify `WriteRoles = ["Admin", "Editor"]` (currently articles); cannot delete or access system settings |
| `User` | Authenticated user with no admin access; reserved for future public-facing features |

Role checks are enforced at two layers:
1. **Route/component level:** `[Authorize(Roles = "Admin")]` on the admin Blazor pages (and admin page
   controllers) prevents any non-admin from accessing admin routes
2. **Handler level:** the per-handler `WriteRoles` check in save/delete blocks the write if the user
   lacks the required role

---

## 2. `UserService`

`UserService` is a **singleton** that wraps `IHttpContextAccessor` for role checking.

```csharp
public class UserService
{
    public bool IsUserAdmin =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true &&
        _httpContextAccessor.HttpContext.User.IsInRole("Admin");

    public bool IsUserAuthor =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true &&
        (_httpContextAccessor.HttpContext.User.IsInRole("Admin") ||
         _httpContextAccessor.HttpContext.User.IsInRole("Editor"));
}
```

**When to use:**
- In Razor components, to conditionally show/hide admin controls (e.g., edit buttons, zone edit overlays)
- In the content-zone editor and admin pages, to gate editing affordances
- Do not use for authorization enforcement — use `[Authorize]` on components/controllers and the
  per-handler `WriteRoles` check for writes

**Injection:** Inject `UserService` directly (it is a singleton, not an interface, by convention for this simple helper).

---

## 3. `DevEmailSender`

Registered in DI (DEBUG builds only) as `IEmailSender`:

```csharp
services.AddSingleton<IEmailSender, DevEmailSender>();
```

When Identity needs to send a confirmation email, `DevEmailSender` logs the message via Serilog instead of sending it. This avoids SMTP configuration requirements in development.

In production, register a real `IEmailSender` implementation before calling `AddWebWayCms` — DI registrations added by the host project take precedence over CMS-registered defaults if the Web project registers first.

---

## 4. Admin User Seeding

`EnsureCmsRolesAndAdminSeeded` (called by `EnsureCMS`) is idempotent:

1. Creates roles `Admin`, `Editor`, `User` if they do not exist
2. Reads `AdminUser:Email` and `AdminUser:Password` from configuration (user-secrets in development)
3. Creates the admin user with `EmailConfirmed = true` if the email is not already registered
4. Adds the admin user to the `Admin` role if not already assigned

**Required secrets:**
```
AdminUser:Email     = admin@example.com
AdminUser:Password  = (must meet password policy)
```

If either secret is missing, seeding is skipped with a warning logged. The application still starts; you must seed manually or provide the secrets.

Seeding is skipped entirely if `WEBWAYCMS_SKIP_ROLESEED=true`.

---

## 5. Password Policy

Configured in `ServiceCollectionExtensions.ConfigureAuthorization`:

```csharp
identityOptions.Password.RequireDigit = true;
identityOptions.Password.RequireLowercase = true;
identityOptions.Password.RequireNonAlphanumeric = true;
identityOptions.Password.RequireUppercase = true;
identityOptions.Password.RequiredLength = 12;
identityOptions.SignIn.RequireConfirmedEmail = true;
```

Minimum 12 characters; requires digits, lower, upper, and a non-alphanumeric character. Email confirmation is required before login — this is bypassed for the seeded admin user (`EmailConfirmed = true` is set directly on the seeded user entity).

---

## 6. Identity UI — Blazor Components

The Identity UI is a set of **Blazor components**, not scaffolded Razor Pages. `.AddDefaultUI()` and the
`Areas/Identity/Pages` tree are gone. The components live in
`WebWayCMS.Presentation/Components/Account/` and route under `/Account/*`:

```
Components/Account/
    Pages/
        Login.razor · Register.razor · RegisterConfirmation.razor
        ForgotPassword.razor · ForgotPasswordConfirmation.razor · ResetPassword.razor · ResetPasswordConfirmation.razor
        ConfirmEmail.razor · ConfirmEmailChange.razor · ResendEmailConfirmation.razor · InvalidPasswordReset.razor
        Login.razor · LoginWith2fa.razor · LoginWithRecoveryCode.razor · Lockout.razor
        ExternalLogin.razor · AccessDenied.razor
        Manage/  (Index, Email, ChangePassword, SetPassword, TwoFactorAuthentication, EnableAuthenticator,
                  Disable2fa, ResetAuthenticator, GenerateRecoveryCodes, ExternalLogins,
                  PersonalData, DeletePersonalData)
    Shared/  (AccountLayout, ManageLayout, ManageNavMenu, ExternalLoginPicker, StatusMessage,
              ShowRecoveryCodes, RedirectToLogin)
```

**Wiring (in `ServiceCollectionExtensions` / `CMSExtensions`):**
- `ConfigureApplicationCookie(...)` points the cookie handler at the Blazor routes:
  ```csharp
  options.LoginPath = "/Account/Login";
  options.LogoutPath = "/Account/Logout";
  options.AccessDeniedPath = "/Account/AccessDenied";
  ```
- `AddCmsBlazorIdentity()` registers the supporting services:
  - `IdentityRevalidatingAuthenticationStateProvider` — server auth-state provider with periodic revalidation
  - `IdentityRedirectManager` — safe redirects + status-message cookie between Identity components
  - `IdentityUserAccessor` — resolves the current `IdentityUser` (redirects to login/invalid pages on failure)
  - `IdentityEmailSender` — typed `IEmailSender<IdentityUser>` wrapper over the configured `IEmailSender`
  - cascading authentication state for the component tree
- `MapAdditionalIdentityEndpoints(app)` (in `ConfigureMiddleware`) maps the non-component Identity
  endpoints (e.g. logout, external-login callbacks, personal-data download) that the Static-SSR account
  components post to. The account components themselves are routable `@page` components served by
  `MapRazorComponents<App>()`.

To customize the Identity UI in a host, replace/override the relevant Blazor components rather than
scaffolding Razor Pages.
