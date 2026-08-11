# Area 8: Identity & Authentication

**Namespaces:**
- `WebWayCMS.Data.DbContexts` — `CmsDbContext`
- `WebWayCMS.Services` — `UserService`, `DevEmailSender`
- `WebWayCMS.Areas.Identity` — scaffolded ASP.NET Identity Razor Pages (in `WebWayCMS.Presentation`)
- `WebWayCMS` — `AuthRateLimiting`

**Depends on:** ASP.NET Identity, EF Core (`CmsDbContext`), ASP.NET Core rate limiting
**Consumed by:** All admin controllers (`[Authorize(Roles = "Admin")]`), `UserService` consumed in views and admin write checks, `CmsIdentitySeeder` for seeding

> Roles and the admin user are seeded only by `UseWebWayCmsAdmin`. A rendering-only host still has
> Identity wired up (login, cookies, lockout, rate limiting) but seeds no roles and no admin user.

---

## 1. Role Model

Three roles are seeded at startup:

| Role | Capabilities |
|------|-------------|
| `Admin` | Full access to all admin routes; write access to all content types; access to destructive operations (delete, version delete) |
| `Editor` | Read access to admin UI; write access to content types that specify `WriteRoles = ["Admin", "Editor"]` (currently articles); cannot delete or access system settings |
| `User` | Authenticated user with no admin access; reserved for future public-facing features |

Role checks are enforced at two layers:
1. **Controller level:** `[Authorize(Roles = "Admin")]` on `AdminContentController` prevents any non-admin from accessing admin routes
2. **Handler level:** `HasWriteAccess(handler.WriteRoles)` in write actions checks the per-handler `WriteRoles` and returns 403 if the user lacks the required role

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
- In Razor views, to conditionally show/hide admin controls (e.g., edit buttons, zone edit overlays)
- In `ContentZoneViewComponent` to determine whether to render `editMode` controls
- Do not use for authorization enforcement — use `[Authorize]` and `HasWriteAccess` in controllers

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

`EnsureCmsRolesAndAdminSeeded` (called by `UseWebWayCms`) is idempotent:

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

## 5. Password Policy, Lockout, and Cookie Hardening

All configured in `ServiceCollectionExtensions.ConfigureAuthorization`:

```csharp
services.AddDefaultIdentity<IdentityUser>(identityOptions =>
{
    identityOptions.SignIn.RequireConfirmedEmail = true;

    identityOptions.Password.RequireDigit = true;
    identityOptions.Password.RequireLowercase = true;
    identityOptions.Password.RequireNonAlphanumeric = true;
    identityOptions.Password.RequireUppercase = true;
    identityOptions.Password.RequiredLength = 12;

    identityOptions.Lockout.AllowedForNewUsers = true;
    identityOptions.Lockout.MaxFailedAccessAttempts = 5;
    identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<CmsDbContext>()
    .AddDefaultUI();

services.ConfigureApplicationCookie(cookieOptions =>
{
    cookieOptions.Cookie.HttpOnly = true;
    cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
});
```

- **Passwords** — minimum 12 characters; requires digits, lower, upper, and a non-alphanumeric character.
- **Email confirmation** is required before login. This is bypassed for the seeded admin user
  (`EmailConfirmed = true` is set directly on the seeded user entity).
- **Lockout** — 5 failed attempts locks the account for 15 minutes, including brand-new users.
- **Auth cookie** — `HttpOnly` (no JS access), `Secure` always (never sent over plain HTTP), and
  `SameSite=Strict` (not sent on cross-site navigations).

---

## 6. Auth Endpoint Rate Limiting

`AuthRateLimiting` throttles the credential-accepting and email-triggering Identity endpoints per
client IP, to slow credential brute force and password-reset email flooding.

- **Limit:** `PermitLimit = 5` requests per `Window = 1 minute`, fixed window, partitioned on
  `HttpContext.Connection.RemoteIpAddress` (`"unknown"` when unavailable)
- **Limited paths** (prefix match, case-insensitive): `/Identity/Account/Login`,
  `/Identity/Account/Register`, `/Identity/Account/ForgotPassword`,
  `/Identity/Account/ResendEmailConfirmation`
- **Everything else** gets `RateLimitPartition.GetNoLimiter("unlimited")` — no throttling
- **Over the limit** returns HTTP **429**

Wired by `ConfigureRateLimiting` (`AddRateLimiter` with `GlobalLimiter = AuthRateLimiting.GetPartition`)
and activated by `UseRateLimiter()` in the shared middleware pipeline, immediately after
`UseRouting()`. The path matching and partition selection are pure static methods so they can be
unit-tested without a server.

Because the partition key is the connection's remote IP, `UseForwardedHeaders()` running first in
the pipeline is what makes this correct behind a reverse proxy.

---

## 7. Identity UI Area

`AddDefaultUI()` in `ServiceCollectionExtensions` embeds ASP.NET Identity's default Razor Pages. The CMS ships scaffolded versions of the most commonly customized pages:

```
Areas/Identity/Pages/Account/
    Login.cshtml.cs
    Logout.cshtml.cs
    Register.cshtml.cs
    ForgotPassword.cshtml.cs
    ForgotPasswordConfirmation.cshtml.cs
    ConfirmEmail.cshtml.cs
    ResetPassword.cshtml.cs
    ResetPasswordConfirmation.cshtml.cs
    ResendEmailConfirmation.cshtml.cs
    Manage/
        Index.cshtml.cs
        ChangePassword.cshtml.cs
        SetPassword.cshtml.cs
        DeletePersonalData.cshtml.cs
        PersonalData.cshtml.cs
        ExternalLogins.cshtml.cs
        TwoFactorAuthentication.cshtml.cs
        ManageNavPages.cs
```

These pages live in **`WebWayCMS.Presentation`** (they did not move to `WebWayCMS.Admin`), are compiled into that assembly, and are served via `CompiledRazorAssemblyPart`. Both bootstrap modes therefore ship login and account management. To customize them in the Web project, scaffold the specific page(s) into `MySite/Areas/Identity/Pages/` — Web project views take precedence over CMS library views.
