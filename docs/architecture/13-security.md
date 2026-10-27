# Area 13: Security

**Namespaces:**
- `WebWayCMS` — `CspOptions`, `CspPolicyBuilder`, `AuthRateLimiting`, the security-header middleware in `CmsMiddlewarePipeline`
- `WebWayCMS.Security` — `RichTextSanitizer`, `ModelValidator`
- `WebWayCMS.Mcp` — `McpApiKeyEndpointFilter` (covered in [Area 12](12-mcp-server.md))

**Depends on:** `Ganss.Xss` (`HtmlSanitizer`), ASP.NET Core rate limiting, ASP.NET Identity
**Consumed by:** every request (the header middleware) and every content save (the sanitizer)

This area collects the cross-cutting defences. Identity's password policy, lockout, and cookie
flags are documented in [Area 8](08-identity-auth.md); the MCP bearer-token boundary in
[Area 12](12-mcp-server.md).

---

## 1. Rich-Text Sanitization

CKEditor HTML is sanitized **server-side on save**, so stored content is safe to render with
`@Html.Raw`.

`RichTextSanitizer` (`WebWayCMS.Core/Security/RichTextSanitizer.cs`) is a static class wrapping a
single reused `Ganss.Xss.HtmlSanitizer`. `Sanitize(object viewModel)` reflects over the model and
rewrites, in place, every property that is:

- a `string`
- readable and writable
- marked `[FormProperty(EditorType = EditorType.RichText)]`

Reflection results are cached per type behind a lock. `SanitizeHtml(string)` is also exposed for
one-off fragments.

**The choke point.** `AdminCrudModel<T>.SaveUpsertAsync` is non-virtual and does exactly this:

```csharp
public async Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
{
    RichTextSanitizer.Sanitize(model);

    var validation = ModelValidator.Validate(model);
    if (validation != null)
        return validation;

    using var _ = _changeSetScope.Begin(ChangeSetKind.Save, null, null);
    return await SaveUpsertCoreAsync(model, ct);
}
```

Subclasses implement the abstract `SaveUpsertCoreAsync` and cannot bypass sanitization. Because
both `AdminContentController` and the MCP toolsets call `SaveUpsertAsync`, **every write path is
covered by construction** — including content types added later.

### Render-time sanitization for raw head-tag fields (exception)

Fields declared `EditorType.TextArea` deliberately bypass the save-time pipeline above (they are
intended for plain text, code, or raw markup a site author controls), so the *"safe to render with
`@Html.Raw`"* guarantee does **not** hold for them at save time. One model in the CMS uses such
fields with `@Html.Raw` output: `GenericPageConfiguration.Meta` and `GenericPageConfiguration.Style`
(Area 3). Both are injected into the page head by `GenericPage/Index.cshtml` and
`GenericAdminPage/Index.cshtml` via `@Html.Raw`.

To close the stored-XSS gap this creates, both controllers hand the view
`GenericPageConfiguration.SanitizedForRender()`, a copy whose fields went through
`HeadMarkupSanitizer` (`WebWayCMS.Core/Security/HeadMarkupSanitizer.cs`) **at render time**, so values
stored earlier are covered too. The stored value is untouched. Each field gets rules for what it
holds — `RichTextSanitizer`'s body-content allow-list would strip every `<meta>` and HTML-encode CSS:

- **`Meta` → `SanitizeMeta`**: a dedicated `HtmlSanitizer` that keeps only `<meta>` and `<link>` with
  descriptive attributes (`name`, `content`, `property`, `charset`, `itemprop`, `rel`, `href`,
  `hreflang`, `type`, `sizes`, `media`, `title`). Scripts, other tags, event handlers, `http-equiv`
  (a `refresh` can redirect) and non-http(s) `href`s are removed.
- **`Style` → `SanitizeCss`**: the CSS is emitted verbatim inside `<style>` (raw text — entities are not
  decoded, so HTML-encoding would corrupt `>` combinators and `&` in strings), except that `</` becomes
  `<\/`, which CSS reads as the same characters but which cannot end the element.

Any new content type that renders a `TextArea` field with `@Html.Raw` must apply the same kind of
render-time neutralization — the save-time choke point will not cover it.

### Widget configuration JSON

Zone items store their widget configuration as JSON (`ComponentPropertiesJson`), outside the upsert
view-model choke point, yet widgets render their `RichText` config fields with `@Html.Raw`. Every
zone-item write therefore passes the payload through `WidgetConfigurationJson.Sanitize`
(`WebWayCMS.Core/Models/ContentZone/`): the zone editor API (`POST /api/contentzones/items`, create and
update) and `ContentZoneChildHandler` (admin child form, MCP `create_child`/`update_child`, the content
seeder, version restore). It resolves the component's configuration type and rewrites **only** the
values of its `RichText` string properties (matched case-insensitively, honouring `[JsonPropertyName]`)
in place: keys the type doesn't declare are kept, a payload with nothing to clean is stored verbatim,
and a rewritten one is serialized with relaxed escaping so markup isn't inflated to `\u003C` against
the 4000-char column. An unregistered component (no type) is stored as-is; a payload that is not a
readable JSON object (malformed, an array, duplicate keys) **fails closed** to `"{}"`.

## 2. Required-Field Validation (same choke point)

`ModelValidator` (`WebWayCMS.Core/Security/ModelValidator.cs`) runs in the same save choke point,
**after** sanitization and **before** the change-set scope opens, so content stripped to empty by the
sanitizer fails `[Required]` and a rejected save never creates a change-set row. It enforces exactly
the fields `describe_content_type` advertises as `required`: DataAnnotations via
`Validator.TryValidateObject(validateAllProperties: true)`, plus `FormPropertyAttribute.IsRequired`
(for view models such as `ContentZoneItemUpsertViewModel` that carry no DataAnnotations). The result's
`ErrorField` is the PascalCase property name, matching the `ModelState` keys the admin controller binds
with.

The admin UI validates first via MVC binding, so this model-side check is a no-op there (the controller
short-circuits before the save); it is what makes MCP writes — which call `SaveUpsertAsync` directly —
honour the same contract rather than silently storing an empty row.

---

## 3. Form Attribute Encoding

All HTML input attributes (placeholder, pattern, value, data-*, aria-*) are built centrally by
`FormAttributeBuilder` (`WebWayCMS.Forms/Forms/FormAttributeBuilder.cs`), which encodes every value
exactly once via `System.Text.Encodings.Web.HtmlEncoder.Default`. The 16 `Form*` view components
emit the result with `@Html.Raw(...)`, so values are encoded at a single unit-tested choke point
rather than per-view string concatenation — eliminating double-encoding risks and attribute-injection
vectors.

## 4. Content-Security-Policy

The CSP header is emitted by the shared middleware in `CmsMiddlewarePipeline.ConfigureSharedMiddleware` and
is host-configurable through the `"Csp"` section.

### `CspOptions`

| Key | Type | Default | Description |
|---|---|---|---|
| `Csp:Enabled` | `bool` | `true` | Emit the header at all |
| `Csp:ReportOnly` | `bool` | `false` | Emit `Content-Security-Policy-Report-Only` instead — monitor without enforcing |
| `Csp:Directives` | `Dictionary<string,string>` | `{}` | Per-directive overrides merged over the CMS defaults |

### CMS defaults

`CspPolicyBuilder.Defaults`, in emission order:

| Directive | Value |
|---|---|
| `default-src` | `'self'` |
| `script-src` | `'self' https://cdn.ckeditor.com` |
| `style-src` | `'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.ckeditor.com` |
| `img-src` | `'self' data: https://cdn.ckeditor.com` |
| `font-src` | `'self' https://cdnjs.cloudflare.com` |
| `connect-src` | `'self'` |
| `object-src` | `'none'` |
| `base-uri` | `'self'` |
| `frame-ancestors` | `'none'` |

These are chosen so the admin UI works with zero configuration: CKEditor from its CDN and Font Awesome
from cdnjs. Bulma is served locally (`~/_content/WebWayCMS.Presentation/css/bulma.min.css`), so it
needs no CDN allowance.

**Preview framing exception.** Responses under `/_preview` (`PreviewConstants.PathPrefix`, the
Admin/Editor-only draft preview) use `CspPolicyBuilder.BuildForPreview`, identical except the default
`frame-ancestors` is `'self'`, and send `X-Frame-Options: SAMEORIGIN` — so the admin page zone editor
can embed the preview in an iframe. A host-supplied `frame-ancestors` directive still wins. Both policy
strings are built once at startup. Every other path keeps `'none'` / `DENY`.

**`script-src` deliberately allows no `'unsafe-inline'`.** Keep admin scripts in files, never in
inline `<script>` blocks. This is why the CKEditor license key reaches the browser through a
`<meta name="ckeditor-license-key">` tag that `admin.js` reads, rather than an inline assignment.

`style-src` does allow `'unsafe-inline'`, because CKEditor injects styles at runtime and views use
inline `style` attributes.

### Merge semantics

`CspPolicyBuilder.Build(options)`:
- returns `""` when `Enabled` is false, and the middleware then emits no header at all
- starts from the defaults in order, then applies `Directives` over them
- a directive the host does not mention **keeps the CMS default**
- a directive set to an **empty or whitespace value is dropped** from the policy
- a directive the defaults do not contain is **appended** in the order the host declared it

```jsonc
"Csp": {
  "Directives": {
    "script-src": "'self' https://cdn.ckeditor.com https://my-cdn.example",  // override
    "frame-ancestors": "",                                                   // drop
    "report-uri": "https://csp.example/report"                               // add
  }
}
```

The header name and value are computed **once at startup** from `IOptions<CspOptions>`, then written
on every response — changing configuration requires a restart. `CspPolicyBuilder` is pure logic and
unit-tested to the 100% gate; the header write itself lives in the pipeline.

---

## 5. Fixed Security Headers

Written on every response by the same middleware, unconditionally:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` (`SAMEORIGIN` under `/_preview` — see the preview framing exception above) |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` |

`UseHsts()` and `UseHttpsRedirection()` run before it — with only `UseSerilogRequestLogging()` in
between — so `Strict-Transport-Security` is present too.

---

## 6. Auth Endpoint Rate Limiting

`AuthRateLimiting` throttles the Identity endpoints that accept credentials or trigger emails, per
client IP **and per endpoint family**: **5 requests per 1-minute fixed window** per
`"{ip}|{matchedPrefix}"` partition, returning HTTP **429** over the limit. Partitioning by matched
prefix keeps an external-login round trip (four requests across four prefixes) from exhausting the
budget of a password login behind the same NAT egress IP. Every other path is explicitly unlimited.

Full details — limited paths (including `/Identity/Account/ResetPassword` and the anonymous
`/Identity/Account/PasskeyRequestOptions`), partitioning, and the `UseForwardedHeaders` interaction that
makes it correct behind a proxy — are in [Area 8](08-identity-auth.md#6-auth-endpoint-rate-limiting).

---

## 7. Authentication Cookie `SameSite`

The Identity application cookie is `HttpOnly`, `Secure=Always`, and `SameSite=Lax` (configured in
`CmsIdentityRegistration.ConfigureAuthorization`).

- **Why `Lax`, not `Strict`:** Chromium computes `SameSite` across the entire redirect chain. An OAuth
  sign-in begins at the provider, so the final navigation back to the app is treated cross-site;
  `Strict` would withhold the cookie and land the user apparently signed out until a manual refresh.
  `Lax` allows the cookie on top-level cross-site *navigations* (GET) so the OAuth return is signed in.
- **Why `Lax` is still safe here:** `Lax` still withholds the cookie on cross-site **POSTs**. That is
  what keeps the passkey minimal-API endpoints (`PasskeyAssertion`, etc.) safe from cross-site
  request forgery without antiforgery middleware — an attacker's cross-site `<form>` POST cannot carry
  the authenticated session cookie.

---

## 8. CKEditor License Key

The admin editor loads CKEditor 5 from `https://cdn.ckeditor.com/ckeditor5/46.1.1/`, and only on
views that define the `CKEditor` Razor section.

The license key is supplied by the **host** through `CKEditor:LicenseKey`. There is no options
class and no DI wiring: `_AdminLayout.cshtml` injects `IConfiguration` and emits

```html
<meta name="ckeditor-license-key" content="@Configuration["CKEditor:LicenseKey"]" />
```

which `WebWayCMS.Admin/wwwroot/js/admin.js` reads (falling back to
`window.__APP_CONFIG__.ckEditorLicenseKey`). Empty or missing ⇒ CKEditor evaluation mode.

The meta tag exists precisely so the key never needs an inline `<script>`, which would force
`'unsafe-inline'` into `script-src`. A CKEditor license key is a JWT that ships to the browser
regardless, so — unlike the MCP `ApiKey` — it is not a server-side secret.

---

## 9. Checklist for a Production Host

- [ ] `ConnectionStrings:DefaultConnection` in user-secrets or environment, not `appsettings.json`
- [ ] `AdminUser:Password` in user-secrets or environment; rotate after first boot
- [ ] `Mcp:Enabled` left `false` unless you genuinely need it; if enabled, treat `Mcp:ApiKey` as a
      root credential and restrict network access to the endpoint
- [ ] Configure `Smtp:*` (host, credentials, from address) so account confirmation and password-reset
      emails are actually delivered; supply `Smtp:Password` via user-secrets/environment. Leaving
      `Smtp:Host` unset means confirmation links are only written to the log — useful in development,
      not in production.
- [ ] If enabling external login, supply `Authentication:{Google,Microsoft,GitHub}:ClientId`/`:ClientSecret`
      via user-secrets/environment; leave a provider's keys unset to keep it disabled
- [ ] For each enabled provider, register the production callback URL on the OAuth app itself —
      `https://yourhost.example/signin-google` / `/signin-microsoft` / `/signin-github`. The provider
      matches `redirect_uri` as an exact string and rejects the request before the browser returns, so
      a missing entry fails with nothing in the CMS logs. See
      [Area 8](08-identity-auth.md#redirect-uris-to-register-with-each-provider); each origin the app
      is reachable on (dev, integration host, production) needs its own entry, and
      `X-Forwarded-Proto` must reach the app or the generated `redirect_uri` will be `http://`.
- [ ] If enabling passkeys, set `Passkeys:ServerDomain` to the production host name (the app otherwise
      infers it from the request `Host` header, which is not trustworthy behind a proxy)
- [ ] Review `Csp:Directives` for any CDN or analytics host your branding adds; validate with
      `Csp:ReportOnly` first, then enforce
- [ ] Serve over HTTPS — the auth cookie is `Secure=Always` and will not be sent otherwise, and
      WebAuthn passkey operations only work over a secure context
- [ ] If terminating TLS at a proxy, configure which proxies are trusted (see
      [Forwarded headers](#forwarded-headers) below) and confirm `X-Forwarded-For` / `X-Forwarded-Proto`
      reach the app, or rate limiting will partition every request onto the proxy's IP
- [ ] Consider running the public host in rendering-only mode — see [Area 11](11-deployment-modes.md)

### Forwarded headers

`X-Forwarded-For` / `X-Forwarded-Proto` are honoured only from trusted proxies
(`CmsHttpInfrastructureRegistration.ConfigureForwardedHeaders`). The default is the framework's
**loopback-only** list, so a client cannot spoof its IP (and dodge the per-IP rate limits) or scheme.
A host behind a proxy on another machine or container must opt in, from the `"ForwardedHeaders"`
config section:

| Key | Effect |
|---|---|
| `KnownProxies` | Array of proxy IP addresses; replaces the loopback default |
| `KnownIPNetworks` | Array of CIDR ranges (e.g. `10.0.0.0/24`); replaces the loopback default |
| `TrustAllProxies` | `true` accepts the headers from **any** source. Only safe when nothing but the proxy can reach the app |

**Upgrading:** earlier versions trusted every source. A deployment behind a remote proxy that sets
none of these keys now ignores the proxy's headers — `UseHttpsRedirection` sees `http` (redirect
loops), OAuth `redirect_uri`s are generated as `http://`, and every client shares the proxy's
rate-limit bucket. The shipped `docker-compose.yml` sets `ForwardedHeaders__TrustAllProxies=true`,
since nginx runs in its own container with no fixed address and `web` only exposes port 8080 on the
compose network.

### Passkey trust posture

Passkey sign-in verifies the authenticator's assertion against the stored credential (a signed
challenge), so it does not trust the client. However, **attestation statements are not validated** by
default (Identity's `IdentityPasskeyOptions.VerifyAttestationStatement` is null). This means the app
does not cryptographically prove *which* authenticator produced the credential — the same trust
posture as the OAuth providers in [Area 8](08-identity-auth.md#8-external-login-google--microsoft--github),
and acceptable for this app's threat model, but worth noting explicitly as a documented Identity
limitation rather than an oversight.
