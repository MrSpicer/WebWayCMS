# Area 13: Security

**Namespaces:**
- `WebWayCMS` — `CspOptions`, `CspPolicyBuilder`, `AuthRateLimiting`, the security-header middleware in `CmsMiddlewarePipeline`
- `WebWayCMS.Security` — `RichTextSanitizer`
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

**The choke point.** `AdminCrudModel<T>.SaveUpsertAsync` is non-virtual and does exactly two things:

```csharp
public Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default)
{
    RichTextSanitizer.Sanitize(model);
    return SaveUpsertCoreAsync(model, ct);
}
```

Subclasses implement the abstract `SaveUpsertCoreAsync` and cannot bypass sanitization. Because
both `AdminContentController` and the MCP toolsets call `SaveUpsertAsync`, **every write path is
covered by construction** — including content types added later.

---

## 2. Content-Security-Policy

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
| `style-src` | `'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://cdn.ckeditor.com` |
| `img-src` | `'self' data: https://cdn.ckeditor.com` |
| `font-src` | `'self' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net` |
| `connect-src` | `'self'` |
| `object-src` | `'none'` |
| `base-uri` | `'self'` |
| `frame-ancestors` | `'none'` |

These are chosen so the admin UI works with zero configuration: CKEditor from its CDN, Bulma from
jsDelivr, Font Awesome from cdnjs.

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

## 3. Fixed Security Headers

Written on every response by the same middleware, unconditionally:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` |

`UseHsts()` and `UseHttpsRedirection()` run immediately before it, so `Strict-Transport-Security` is
present too.

---

## 4. Auth Endpoint Rate Limiting

`AuthRateLimiting` throttles the Identity endpoints that accept credentials or trigger emails, per
client IP: **5 requests per 1-minute fixed window**, returning HTTP **429** over the limit. Every
other path is explicitly unlimited.

Full details — limited paths, partitioning, and the `UseForwardedHeaders` interaction that makes it
correct behind a proxy — are in [Area 8](08-identity-auth.md#6-auth-endpoint-rate-limiting).

---

## 5. CKEditor License Key

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

## 6. Checklist for a Production Host

- [ ] `ConnectionStrings:DefaultConnection` in user-secrets or environment, not `appsettings.json`
- [ ] `AdminUser:Password` in user-secrets or environment; rotate after first boot
- [ ] `Mcp:Enabled` left `false` unless you genuinely need it; if enabled, treat `Mcp:ApiKey` as a
      root credential and restrict network access to the endpoint
- [ ] Review `Csp:Directives` for any CDN or analytics host your branding adds; validate with
      `Csp:ReportOnly` first, then enforce
- [ ] Serve over HTTPS — the auth cookie is `Secure=Always` and will not be sent otherwise
- [ ] If terminating TLS at a proxy, confirm `X-Forwarded-For` / `X-Forwarded-Proto` reach the app,
      or rate limiting will partition every request onto the proxy's IP
- [ ] Consider running the public host in rendering-only mode — see [Area 11](11-deployment-modes.md)
