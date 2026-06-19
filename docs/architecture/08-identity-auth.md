# Area 8: Identity & Authentication

**Namespaces:**
- `WebWayCMS.Data.DbContexts` — `ApplicationDbContext`
- `WebWayCMS.Services` — `UserService`, `DevEmailSender`
- `WebWayCMS.Areas.Identity` — scaffolded ASP.NET Identity Razor Pages

**Depends on:** ASP.NET Identity, EF Core (`ApplicationDbContext`)
**Consumed by:** All admin controllers (`[Authorize(Roles = "Admin")]`), `UserService` consumed in views and admin write checks, `CMSExtensions` for seeding

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

## 6. Identity UI Area

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

These pages are compiled into the CMS assembly and served via `CompiledRazorAssemblyPart`. To customize them in the Web project, scaffold the specific page(s) into `MySite/Areas/Identity/Pages/` — Web project views take precedence over CMS library views.
