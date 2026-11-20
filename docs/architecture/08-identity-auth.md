# Area 8: Identity & Authentication

**Namespaces:**
- `WebWayCMS.Data.DbContexts` — `CmsDbContext`
- `WebWayCMS.Services` — `UserService`
- `WebWayCMS.Identity` — `SmtpEmailSender`, `LoggingEmailSender`, `SmtpOptions`, `GoogleAuthOptions`, `MicrosoftAuthOptions`, `GitHubAuthOptions`
- `WebWayCMS.Areas.Identity` — scaffolded ASP.NET Identity Razor Pages (in `WebWayCMS.Presentation`)
- `WebWayCMS` — `AuthRateLimiting`, `CmsPasskeyEndpoints`

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

## 3. Email Sending (`SmtpEmailSender` / `LoggingEmailSender`)

`CmsIdentityRegistration.ConfigureEmailSender` registers a single `IEmailSender` chosen by whether
`Smtp:Host` is set:

```csharp
services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

var senderType = string.IsNullOrWhiteSpace(configuration["Smtp:Host"])
    ? typeof(LoggingEmailSender)
    : typeof(SmtpEmailSender);

// AddDefaultIdentity seeds a NoOpEmailSender IEmailSender before this runs, so TryAdd* alone
// would be a no-op. Replace that framework default with the real sender, but leave a
// host-registered IEmailSender untouched (a host that registers its own sender keeps it).
var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
if (existing is null || existing.ImplementationType == typeof(NoOpEmailSender))
    services.Replace(ServiceDescriptor.Singleton(typeof(IEmailSender), senderType));
```

- **`SmtpEmailSender`** is a real sender built on the BCL's `System.Net.Mail.SmtpClient` — no third-party
  package. It reads the `Smtp` section and, when `SendEmailAsync` is called, builds an HTML
  `MailMessage` and delivers it over SMTP. It throws an `InvalidOperationException` naming the missing
  key if `Smtp:Host` **or** `Smtp:FromAddress` is empty at send time.
- **`LoggingEmailSender`** is the fallback when `Smtp:Host` is blank (any dev box or
  `scripts/StartIntegrationHost.sh`). It never throws: it logs the recipient, subject, and full message
  to Serilog, plus a `Warning` that the message was **not delivered** — so registration succeeds and the
  confirmation link is recoverable from the log instead of a 500 with a half-created user.

**Host precedence:** `AddDefaultIdentity` itself registers a `NoOpEmailSender` `IEmailSender`; the CMS
replaces that default with the sender chosen above, but leaves a host that has already registered its
own `IEmailSender` untouched — the CMS never overrides an explicit host registration.

**Config keys (`Smtp` section):**

| Key | Type | Default | Description |
|---|---|---|---|
| `Smtp:Host` | `string` | — | SMTP server host. Blank ⇒ `LoggingEmailSender` (log-only). |
| `Smtp:Port` | `int` | `587` | SMTP server port (submission over TLS). |
| `Smtp:EnableSsl` | `bool` | `true` | Use TLS (STARTTLS/SMTPS). |
| `Smtp:UserName` | `string` | — | Optional user name for authenticated relays. |
| `Smtp:Password` | `string` | — | Optional password; supply via user-secrets/env. |
| `Smtp:FromAddress` | `string` | — | From address on outgoing mail. Required when `Smtp:Host` is set. |
| `Smtp:FromName` | `string` | — | Optional from display name. |

Because `SignIn.RequireConfirmedAccount = true`, a host **must** configure `Smtp` (or a mail catcher
like smtp4dev in development) for registration confirmation and password-reset emails to actually
be delivered — otherwise they land in the log only.

**General email sending:** `IEmailSender` is also the CMS's general email API — a host site (or CMS
code) that needs to send mail injects `IEmailSender` and gets the same configured sender; there is no
separate email service.

**Send failures never break an account page:** the scaffolded `Register`, `ExternalLogin`,
`ForgotPassword` and `ResendEmailConfirmation` pages send through
`EmailSenderExtensions.TrySendEmailAsync` (`WebWayCMS.Identity`), which catches any exception and logs
an error with the recipient and subject (never the body — it carries the token). An unreachable or
misconfigured SMTP server therefore still lands the user on `RegisterConfirmation` /
`ForgotPasswordConfirmation` (or the Resend page's usual "Verification email sent" message, which is the
same whether or not the account exists); the user retries with Resend / Forgot password once mail works.
Both pages treat a **soft-deleted** user as absent (`IUserAdminService.IsDeletedAsync`): the same answer, no
email.

**Integration host:** `WebWayCMS.TestHost`'s compose stack runs an smtp4dev mail catcher (UI + REST API
on `http://localhost:45852`) and points `Smtp:*` at it; the Playwright `account.write.spec.ts` walks
register → confirm → login, unconfirmed login, resend confirmation, and forgot → reset password by
reading the mail through the smtp4dev API.

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
    identityOptions.SignIn.RequireConfirmedAccount = true;

    identityOptions.Password.RequireDigit = true;
    identityOptions.Password.RequireLowercase = true;
    identityOptions.Password.RequireNonAlphanumeric = true;
    identityOptions.Password.RequireUppercase = true;
    identityOptions.Password.RequiredLength = 12;

    identityOptions.User.RequireUniqueEmail = true;

    // Schema version 3 adds the AspNetUserPasskeys table (passkey/WebAuthn support).
    identityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3;

    identityOptions.Lockout.AllowedForNewUsers = true;
    identityOptions.Lockout.MaxFailedAccessAttempts = 5;
    identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<CmsDbContext>();

services.ConfigureApplicationCookie(cookieOptions =>
{
    cookieOptions.Cookie.HttpOnly = true;
    cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
});
```

- **Passwords** — minimum 12 characters; requires digits, lower, upper, and a non-alphanumeric character.
- **Unique email** — `RequireUniqueEmail = true` rejects a second account registering an email that is
  already in use.
- **Email confirmation** — `RequireConfirmedEmail = true` **and** `RequireConfirmedAccount = true` are both
  set. They are the same predicate (the default `IUserConfirmation<IdentityUser>` checks
  `IsEmailConfirmedAsync`), but setting both is what makes the scaffolded `Register` / `ExternalLogin`
  pages' `RequireConfirmedAccount` gates actually block an unconfirmed user. This is bypassed for the
  seeded admin user (`EmailConfirmed = true` is set directly on the seeded user entity).
- **Lockout** — 5 failed attempts locks the account for 15 minutes, including brand-new users.
- **Auth cookie** — `HttpOnly` (no JS access), `Secure` always (never sent over plain HTTP), and
  `SameSite=Lax` (sent on top-level cross-site navigations — required for OAuth redirect chains — but
  withheld on cross-site POSTs; see [Area 13](13-security.md)).

---

## 6. Auth Endpoint Rate Limiting

`AuthRateLimiting` throttles the credential-accepting and email-triggering Identity endpoints per
client IP **and per endpoint family**, to slow credential brute force and password-reset email flooding.

- **Limit:** `AuthRateLimitOptions.PermitLimit` (default **5**) requests per `WindowSeconds` (default
  **60**), fixed window, partitioned on
  `"{RemoteIpAddress}|{matchedPrefix}"` (`"unknown"` for the IP when unavailable). Each limited path is
  its own bucket, so one endpoint family's traffic never exhausts another's budget.
- **Limited paths** (prefix match, case-insensitive): `/Identity/Account/Login`,
  `/Identity/Account/LoginWith2fa`, `/Identity/Account/LoginWithRecoveryCode`,
  `/Identity/Account/Register`, `/Identity/Account/ForgotPassword`, `/Identity/Account/ResetPassword`,
  `/Identity/Account/ResendEmailConfirmation`, `/Identity/Account/AcceptInvitation`,
  `/Identity/Account/ExternalLogin`, `/Identity/Account/PasskeyAssertion`, `/Identity/Account/PasskeyRequestOptions`,
  `/Identity/Account/Manage/EnableAuthenticator`, `/Identity/Account/Manage/TwoFactorAuthentication`
  — the second-factor endpoints are limited because `LoginWith2fa`/`LoginWithRecoveryCode` are an
  **anonymous** code-verification gate at the end of every sign-in flow (password, external, passkey),
  and the Manage endpoints verify authenticator codes during setup. Segment-boundary matching keeps
  them distinct buckets from `/Login` (`LoginWith2fa` does not start a `/Login` segment).
- **Everything else** gets `RateLimitPartition.GetNoLimiter("unlimited")` — no throttling
- **Over the limit** returns HTTP **429**

`MatchLimitedPath(path)` returns the matched prefix (or `null`); `IsRateLimitedPath` is
`MatchLimitedPath(path) is not null`. A single external sign-up costs four requests (Login GET →
ExternalLogin POST → callback GET → confirmation POST); because those span distinct prefixes, a retry or
a second user behind the same NAT egress IP does not 429 an unrelated login.

**Configuration (`AuthRateLimiting` section, `AuthRateLimitOptions`):**

| Key | Type | Default | Description |
|---|---|---|---|
| `AuthRateLimiting:PermitLimit` | `int` | `5` | Requests allowed per window, per client IP and endpoint family. |
| `AuthRateLimiting:WindowSeconds` | `int` | `60` | Length of the fixed window. |

Both must be at least 1 — the options are validated at startup (`ValidateOnStart`), so a bad value stops
the host from booting. There is no off switch. The integration host raises `PermitLimit` to 100 because
its Playwright suite drives several account flows from one IP.

Wired by `ConfigureRateLimiting` (binds `AuthRateLimitOptions`, then configures `RateLimiterOptions` with
`GlobalLimiter = PartitionedRateLimiter.Create(ctx => AuthRateLimiting.GetPartition(ctx, options))`)
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
    RegisterConfirmation.cshtml.cs
    AcceptInvitation.cshtml.cs   (WebWayCMS addition: an Admin's invitation link, §12)
    ExternalLogin.cshtml.cs
    Manage/
        Index.cshtml.cs
        ChangePassword.cshtml.cs
        SetPassword.cshtml.cs
        DeletePersonalData.cshtml.cs
        DownloadPersonalData.cshtml.cs
        PersonalData.cshtml.cs
        ExternalLogins.cshtml.cs
        TwoFactorAuthentication.cshtml.cs
        EnableAuthenticator.cshtml.cs
        GenerateRecoveryCodes.cshtml.cs
        ShowRecoveryCodes.cshtml.cs
        Disable2fa.cshtml.cs
        ResetAuthenticator.cshtml.cs
        Passkeys.cshtml.cs
        ApiTokens.cshtml.cs   (WebWayCMS addition: MCP personal access tokens)
        ManageNavPages.cs
```

`RegisterConfirmation` (where `Register` and `ExternalLogin` send a user who still has to confirm) is a
WebWayCMS replacement for the Identity UI default: it only says "Please check your email to confirm your
account" with a Resend link. The default page looked the user up from its `email` query value — answering
"Unable to load user" for an unknown address — and could show the raw confirmation link; this one does
neither.

These pages live in **`WebWayCMS.Presentation`** (they did not move to `WebWayCMS.Admin`), are compiled into that assembly, and are served via `CompiledRazorAssemblyPart`. Both bootstrap modes therefore ship login and account management. To customize them in the Web project, scaffold the specific page(s) into `MySite/Areas/Identity/Pages/` — Web project views take precedence over CMS library views.

---

## 8. External Login (Google / Microsoft / GitHub)

Three OAuth providers can be enabled, each gated on its own configuration:

| Provider | Package | Config section |
|---|---|---|
| Google | `Microsoft.AspNetCore.Authentication.Google` | `Authentication:Google` |
| Microsoft Account | `Microsoft.AspNetCore.Authentication.MicrosoftAccount` | `Authentication:Microsoft` |
| GitHub | `AspNet.Security.OAuth.GitHub` (aspnet-contrib) | `Authentication:GitHub` |

Each section has `ClientId` and `ClientSecret` keys, supplied via user-secrets/environment and never
committed. `CmsIdentityRegistration.ConfigureExternalLogins` binds each section to its options class
and calls `.AddGoogle()`/`.AddMicrosoftAccount()`/`.AddGitHub()` **only when that provider's
`ClientId` and `ClientSecret` are both non-empty**. A host that omits a provider's keys simply gets no
button for it — the `Login`/`Register`/`ExternalLogins` UI already degrades gracefully to "no external
services configured" (or hides the link) with zero providers registered.

### Redirect URIs to register with each provider

Configuring `ClientId`/`ClientSecret` is only half the setup. Each OAuth handler owns a
**`CallbackPath`** — the local route the provider sends the browser back to with the authorization
code — and every provider refuses to issue a code unless that exact URL is pre-registered on the OAuth
app. `ConfigureExternalLogins` sets only the client credentials, so the handler defaults apply:

| Provider | Callback path | Where to register it |
|---|---|---|
| Google | `/signin-google` | Cloud Console → APIs & Services → Credentials → OAuth 2.0 Client ID → **Authorized redirect URIs** |
| Microsoft Account | `/signin-microsoft` | Azure Portal → Entra ID → App registrations → Authentication → Web → **Redirect URIs** |
| GitHub | `/signin-github` | Settings → Developer settings → OAuth Apps → **Authorization callback URL** |

These routes are **not** declared anywhere in this codebase — no controller, no Razor page, no
`MapPost`. `UseAuthentication()` (`CmsMiddlewarePipeline.cs`) runs ahead of `UseRouting()` and the
remote handler intercepts its own `CallbackPath` before endpoint matching, so `CMSRouteTransformer`'s
`{**slug}` catch-all never sees them.

At challenge time ASP.NET Core builds the `redirect_uri` parameter from the **current request's scheme
and host** plus that path, and the provider compares it against its registered list as an exact string
— scheme, host, port, path, and trailing slash all count. A mismatch fails at the provider *before the
browser ever returns*, so nothing in the CMS logs it: Google shows `Error 400: redirect_uri_mismatch`,
GitHub shows "The redirect_uri MUST match the registered callback URL". That is the anti-hijacking
control in OAuth — without it, anyone holding the (public) `ClientId` could point an authorization
request at their own server and harvest codes issued for this app.

Because the URI is derived per-request, **each origin the app is reachable on needs its own entry**:

| Origin | Register |
|---|---|
| Dev server (`https://localhost:7046/`) | `https://localhost:7046/signin-google` |
| Integration host (`scripts/StartIntegrationHost.sh`, `http://localhost:45847`) | `http://localhost:45847/signin-google` |
| Production | `https://yourhost.example/signin-google` |

Plain `http` works for the integration host only because all three providers make an explicit
exception for `localhost`. One OAuth app can hold several redirect URIs, so dev and production can
coexist — though separate apps per environment keeps a leaked dev secret away from production.

Behind a TLS-terminating proxy this is where `UseForwardedHeaders` matters: if `X-Forwarded-Proto`
does not reach the app it will build an `http://` `redirect_uri` and the provider will reject it.

### Enabling a provider locally

`WebWayCMS.TestHost/appsettings.json` is git-tracked, so use user-secrets rather than adding the keys
to that file:

```bash
cd WebWayCMS.TestHost
dotnet user-secrets set "Authentication:Google:ClientId"     "<id>"
dotnet user-secrets set "Authentication:Google:ClientSecret" "<secret>"
```

With no keys set, the `Login` page renders "There are no external authentication services configured"
— note that a **misspelled config key is indistinguishable from an intentionally disabled provider**,
since `ConfigureExternalLogins` skips silently.

### Sign-in round trip

The sign-in round trip is the standard scaffolded callback page, `Account/ExternalLogin.cshtml.cs`:
`OnPost` challenges the provider, `OnGetCallback` signs an existing user in via
`ExternalLoginSignInAsync`, and `OnPostConfirmation` creates a new `IdentityUser`, links the login
(`AddLoginAsync`), and sends a confirmation email.

`OnGetCallbackAsync` handles the full sign-in result surface rather than falling through to the
create-account form for non-`Succeeded` results:

- **`Succeeded`** → `LocalRedirect` to the return URL.
- **`RequiresTwoFactor`** → redirect to `LoginWith2fa` (`bypassTwoFactor: false`, so a 2FA-enrolled user
  is challenged on external sign-in just like password sign-in).
- **`IsLockedOut`** → `Lockout`.
- **`IsNotAllowed`** (unconfirmed email) → redirect to `Login` with an `ErrorMessage` pointing at
  *Resend email confirmation*, instead of dead-ending on the create-account form (whose `CreateAsync`
  would fail `DuplicateEmail`).

---

## 9. Two-Factor Authentication (self-service opt-in)

TOTP authenticator support is built into `Microsoft.AspNetCore.Identity` (no extra package). The
`TwoFactorAuthentication` Manage page links to the now-scaffolded sub-pages:

- `EnableAuthenticator` — generates/resets the authenticator key + `otpauth://` URI, verifies a TOTP
  code, enables 2FA, and hands off to `ShowRecoveryCodes`.
- `ShowRecoveryCodes` — display-only page that renders the recovery codes carried across the redirect
  in `[TempData]`.
- `GenerateRecoveryCodes` — regenerates a fresh set of 10 recovery codes.
- `Disable2fa` — `SetTwoFactorEnabledAsync(user, false)`.
- `ResetAuthenticator` — resets the authenticator key and forces re-enrollment.

Scope is self-service opt-in only: there is no enforcement of 2FA for Admin/Editor. Login's existing
`RequiresTwoFactor` branch still redirects to `LoginWith2fa`/`Lockout`, which are served by the
framework default UI.

The `EnableAuthenticator` page shows the shared key, the `otpauth://` URI (as a readonly input for
manual entry), **and a scannable QR code**. The QR code is generated server-side by `QRCoder`
(`SvgQRCode`, pure managed — no `System.Drawing`) and rendered as inline `<svg>`, which needs no CSP
change; the stock scaffold's `qrcode.min.js`/`qr.js` bundle is not used, so no inline `<script>` is
required.

---

## 10. Passkeys (WebAuthn)

Passkeys are supported by the `SignInManager`/`UserManager` passkey APIs added in .NET 10 Identity
(`Microsoft.AspNetCore.Identity` / `.EntityFrameworkCore` 10.0.11 — no extra package). Because
WebWayCMS is an MVC/Razor Pages app rather than the Blazor Web App template that ships this feature's
scaffolding, the endpoints and UI are hand-written against those framework-agnostic APIs.

**Storage.** `CmsDbContext` sets `identityOptions.Stores.SchemaVersion =
IdentitySchemaVersions.Version3`, which is what maps the `AspNetUserPasskeys` table.

**Endpoints** (minimal APIs in `WebWayCMS/Startup/CmsPasskeyEndpoints.cs`, mapped from
`CmsMiddlewarePipeline.MapCmsEndpoints` so both pipelines get them):

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /Identity/Account/PasskeyCreationOptions` | `[Authorize]` | `MakePasskeyCreationOptionsAsync` → creation-options JSON |
| `POST /Identity/Account/PasskeyRegistration` | `[Authorize]` | `PerformPasskeyAttestationAsync` → `AddOrUpdatePasskeyAsync` |
| `POST /Identity/Account/PasskeyRequestOptions` | anonymous | `MakePasskeyRequestOptionsAsync` → request-options JSON |
| `POST /Identity/Account/PasskeyAssertion` | anonymous | `PasskeySignInAsync` → sign-in result |

`PasskeyAssertion` responds with the full result surface rather than a generic 400: `Ok` on success,
`Ok({ requiresTwoFactor = true })` when the user has 2FA enabled (the two-factor user-id cookie is
already issued by `PasskeySignInAsync`, so `passkeys.js` redirects to `LoginWith2fa`), and a
problem-details `title` for `IsLockedOut`/`IsNotAllowed`/generic failure. `PasskeyAssertion` and
`PasskeyRequestOptions` are rate-limited (they accept credentials / mint challenges); the other two are
`[Authorize]`-gated.

**UI.** `Manage/Passkeys.cshtml` lists `GetPasskeysAsync`, triggers the add flow through
`wwwroot/js/passkeys.js` (the `PublicKeyCredential.parseCreationOptionsFromJSON` /
`parseRequestOptionsFromJSON` + `toJSON` Safari polyfill from the Microsoft docs), and posts
rename/remove back to `OnPostRenameAsync`/`OnPostRemoveAsync`. `Login.cshtml` adds a "Sign in with a
passkey" button that runs the request-credential flow and posts the assertion to `PasskeyAssertion`.

**Config.** `Passkeys:ServerDomain` (bound to `IdentityPasskeyOptions`) may be set by a production
host to pin the expected RP domain; it is left unset for development (host-header inference is fine on
`https://localhost:7046/`).

---

## 11. MCP Personal Access Tokens

`Manage/ApiTokens.cshtml` (not scaffolded; a WebWayCMS page in the same area) lets an **Admin** issue and
revoke their own MCP tokens. It is `[Authorize(Roles = "Admin")]` because the MCP endpoint accepts only Admins,
and it 404s (its `_ManageNav` link hidden) on a rendering-only host, where `IApiTokenService` is not registered.
A new token's plaintext is rendered straight from the create POST (never through TempData) with copy buttons
(`wwwroot/js/api-tokens.js`) and a ready-to-paste `.mcp.json`. Tokens live in the `ApiTokens` table (hash only,
FK to `AspNetUsers` with cascade delete). The token itself authenticates through the `McpToken` scheme, which
only the `/mcp` endpoint uses; see [Area 12](12-mcp-server.md).

---

## 12. User Management (Users / User Roles widgets)

Admins list and manage users through two content-zone widgets on admin pages. The CMS seeds a ready-made
**Users** page at `/wadmin/users` (navbar "Users", the Users widget) and a **Roles** page at `/wadmin/roles`
(navbar "Roles", the User Roles widget), as entries in the CMS's JSON seed file
(`cms-pages.contentseed.json`, see [Area 15](15-content-seeding.md) §11). Every action is Admin only: the widgets render nothing for
anyone else and on public pages, and their forms post to an Admin-only controller.

**Storage.** The user type stays `IdentityUser`. CMS data about a user lives 1:1 in **`UserProfiles`**
(`UserProfileDTO`: `DisplayName`, `FirstName`, `LastName`, `LastLoginUtc`, `IsDeleted`/`DeletedUtc`/`DeletedBy`,
`InvitedUtc`/`InvitedBy`; FK to `AspNetUsers` with cascade delete). A user without a row has an empty profile.

**Layers.**

| Piece | Project | Role |
|---|---|---|
| `IUserAdminService` / `UserAdminService` | Data (`Services/Users`) | Identity `UserManager`/`RoleManager` + the profile rows: list (role / status filters, paging), detail, invite, update, roles, confirm, lock/unlock, sign-out-everywhere, reset 2FA, revoke tokens, remove passkeys / external logins, soft delete / restore / purge, role CRUD, `RecordLoginAsync`. One Serilog audit line per write (`User management: {Actor} {Action} {TargetEmail} ({Target})`). |
| `UserManagementGuards` | Data | Never leave no **active** Admin (in the Admin role, not locked, not deleted); no self-destructive action (delete, lock, removing your own Admin role). |
| `IUserManagementModel` / `UserManagementModel` | Core (`Users`) | The service plus the account emails and password re-entry; returns one user-facing message. Used by the widget controller and the MCP tools. |
| `IUserAccountEmails` / `IUserAccountLinks` | Core | Invitation, confirmation, reset and "email changed" emails, sent through `TrySendEmailAsync` (a failed send is reported in the message, never thrown); links from the current request's origin. |
| `AdminUserManagementController` | Admin | `POST /wadmin/usermanagement/…` (one action per operation, antiforgery, local `returnUrl`, TempData flash). |
| `UsersViewComponent`, `UserRolesViewComponent` | Presentation (views in Admin) | The widgets. |
| `UserToolset` | Mcp | The MCP tools. |

**Status.** `UserStatus` is decided in order: **Deleted** (profile flag), **Locked** (`LockoutEnd` in the
future), **Unconfirmed**, **Active**. A user is shown as "Invited" while an invitation is pending: an
`InvitedUtc`, no password, and an unconfirmed email.

**Actions and rules.**
- **Invite:** creates a passwordless, unconfirmed user with the profile and roles (User preselected in the form)
  and emails a link to `/Identity/Account/AcceptInvitation`. That page sets the password and confirms the email
  in one step. The link uses its own token provider, `InvitationTokenProvider` ("Invitation" /
  "AcceptInvitation"), valid `UserManagement:InvitationLifetimeDays` days (default 7, 1–90, `ValidateOnStart`).
  It dies once a password is set, because that rotates the security stamp the token is bound to. A
  locked/deleted account or a bad link gets one generic message. If the password write succeeds but the
  email-confirm write fails, the page says so and points at the admin's Send-confirmation action (the admin
  Users screen can recover that state; resending the invitation cannot — the account has a password now).
  A soft-deleted user's email can't be invited ("restore it, or delete it permanently first").
- **Edit:** email (also the username; a change makes the email **unconfirmed**, sends a confirmation to the new
  address and a notice to the old), phone, profile names. For a user whose **invitation is pending**, an email
  change (it rotates the stamp, killing the old link) sends a **new invitation** instead, and no notice to the
  never-confirmed old address. An Admin can't change **their own** email here (`UserManagementGuards.SelfEmailChange`:
  "Use Account → Email", which keeps the old address until the new one is confirmed), and an email change is
  refused when it would take the last active Admin out of service (an unconfirmed account can't sign in).
- **Never leave no active Admin:** lock, delete, removing the Admin role and an email change are refused when
  the target is the only **active** Admin — in the Admin role, email **confirmed**, not locked, not deleted. An
  invited (unconfirmed) Admin can't sign in under `RequireConfirmedAccount`, so it never counts. No self lock,
  delete, purge or Admin removal either.
- **Lock** sets `LockoutEnd` to `UserAdminService.IndefiniteLockoutEnd` (9999-01-01, not `MaxValue`, which
  `timestamptz` can't hold) and rotates the stamp. **Unlock** clears it and the failed-attempt count.
- **Sign out everywhere**, **Lock**, **Delete** and role changes take effect when the cookie's security stamp
  is next validated (the framework default, every 30 minutes).
- **Delete** is soft: it flags the profile, locks the account, rotates the stamp and revokes API tokens. The user
  is hidden from the list unless filtered by status Deleted. **Restore** undoes it. **Delete permanently**
  (purge) is offered only for a soft-deleted user: `UserManager.DeleteAsync` removes roles, logins, passkeys,
  tokens and profile, and frees the email. Content keeps its `CreatedBy` Guid. A soft-deleted user refuses
  every other write.
- **Password re-entry:** delete, purge and granting Admin (invite or role change) take the acting Admin's
  password, checked with `CheckPasswordSignInAsync(lockoutOnFailure: true)`. An Admin with no password is told to
  set one. MCP calls skip re-entry (the token authenticated the call); the guards still apply.
- **Roles:** built-in Admin/Editor/User (`UserProfileRules.BuiltInRoles`) can't be renamed or deleted. A custom
  role can be deleted only once it has no members.

**Self-registration.** `Register` and `ExternalLogin` add new users to the **User** role
(`UserProfileRules.DefaultRole`); a failed role assignment is surfaced as a form error instead of
signing the user in (the account exists but is unusable until an Admin fixes the role).
`Identity:AllowRegistration` (`IdentityRegistrationOptions`, default true) set to false makes `Register`
answer 404, hides Login's "Register" link, and stops `ExternalLogin` creating accounts (existing linked users
still sign in). **Resend email confirmation stays available** either way: an Admin's email change unconfirms
an existing account, and this is how that user recovers (its answer is the same for every email).

**Last login.** `CmsSignInManager` (registered with `AddSignInManager`) overrides `SignInWithClaimsAsync`, the
step every successful sign-in ends in, and stamps `LastLoginUtc`. `RefreshSignInAsync` (the cookie re-issue
after a password, 2FA or profile change on the Manage pages) ends there too and is **not** recorded. A failure
is logged, never fatal; a failed first-login insert (two concurrent first sign-ins) is detached from the scoped
context so a later save in the same request doesn't retry it.

---

## 13. Personal Data Download

`Manage/PersonalData.cshtml`'s "Download" button posts to `DownloadPersonalData`, now scaffolded in
`WebWayCMS.Presentation`. `DownloadPersonalDataModel.OnPostAsync` reflects over `IdentityUser`'s
`[PersonalData]`-tagged properties, appends the user's external login provider keys and authenticator
key, serializes the result to JSON, and returns it as a file download (`Content-Disposition:
attachment; filename=PersonalData.json`).
