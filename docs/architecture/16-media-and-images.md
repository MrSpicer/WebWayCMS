# Area 16: Media Library and Images

**Namespaces:**
- `WebWayCMS.Data.Models` — `MediaBlobDTO`, `MediaBlobBytesDTO`, `ImageDTO`
- `WebWayCMS.Data.Services` — `IMediaBlobStore` / `MediaBlobStore`, `IMediaLibrary` / `MediaLibrary`
- `WebWayCMS.Security` — `ImageValidator`, `ImageValidationResult`
- `WebWayCMS.Media` — `MediaOptions`, `MediaUrl`
- `WebWayCMS.Models.Image` — `ImageModel`, the view models, `ImageContentZoneConfiguration`
- `WebWayCMS.Controllers` — `MediaController` (public byte serving)
- `WebWayCMS.Controllers.Api` — `MediaApiController` (admin upload/list/delete)
- `WebWayCMS.ViewComponents` — `ImageViewComponent`, `FormImagePicker`
- `WebWayCMS.Services.ContentSeeding` — `SeedMediaResolver`
- `WebWayCMS.Mcp` — `MediaToolset`

**Depends on:** Data, Core, Forms
**Consumed by:** the admin UI, content zones, content seeding, MCP

---

## 1. The Problem

Before this area the CMS could not render an image at all: there was no upload path, no `IFormFile`
anywhere in the solution, no `byte[]` on any CMS entity, and no file editor type. An editor's only
option was to paste an external URL into rich text — which the default CSP
(`img-src 'self' data: https://cdn.ckeditor.com`) then blocks.

## 2. Where the bytes live, and why

Bytes are stored in Postgres (`bytea`), content-addressed by the SHA-256 of the file, behind an
`IMediaBlobStore` seam.

| Decision | Choice | Why |
|---|---|---|
| Medium | Postgres `bytea` | The admin and rendering-only hosts are guaranteed a shared *database*, never a shared filesystem. Disk storage also needs a volume the shipped `docker-compose.yml` does not mount, so uploads would not survive a redeploy. Media stays in the same backup/restore story as content. |
| Addressing | SHA-256 of the bytes | Identical uploads deduplicate for free; a URL's content can never change, so responses are served `immutable`; a new `ContentVersion` reusing an image costs nothing. |
| Swappability | `IMediaBlobStore` | Filesystem or cloud storage is an additive package plus one config line, not a migration. |
| Catalog vs bytes | Two tables | EF materializes every scalar property, so a `Bytes` column on the catalog row would drag megabytes into every library listing. A non-database store also leaves `MediaBlobBytes` empty while the catalog keeps working. |
| Derivatives | None in v1 | Originals only; thumbnails slot into the same store under derived keys later. |

### Tables

| Table | Versioned? | Contents |
|---|---|---|
| `MediaBlobs` | no | `Hash` (PK, `character(64)`), `ContentType`, `ByteLength`, `Width`, `Height`, `OriginalFileName`, `CreatedUtc` |
| `MediaBlobBytes` | no | `Hash` (PK) + `Bytes` (`bytea`) |
| `Images` | yes | `IVersionedContent` + `BlobHash`, `AltText`, `Caption` |

## 3. Two constraints that shaped the design

**Image bytes never touch a view model.** `ContentFieldMerger.TryMerge` does a full
`System.Text.Json` serialize→deserialize round-trip of the upsert view model on *every* MCP
`update_content` and *every* content-seed apply. A `byte[]` property would base64 the whole image
through each partial write. `ImageUpsertViewModel` therefore carries a `BlobHash` **string**, and
uploading is a separate endpoint.

**Widget configuration is capped at 4000 characters**
(`ContentZoneItemDTOEntityConfiguration`). The Image widget stores a node reference, which fits;
inline data never would.

## 4. Validation — no imaging library

`ImageValidator` is static and stateless, matching `RichTextSanitizer` and `ModelValidator`. It:

- identifies the format from **magic bytes** (PNG, JPEG, GIF, WebP) — the declared content type and
  the file extension are both client-controlled and are never trusted;
- **rejects SVG explicitly**, because it is executable content and `img-src 'self'` would not
  contain a script served from the site's own origin;
- enforces `MediaOptions.MaxUploadBytes`;
- reads **pixel dimensions out of the file header** — PNG `IHDR`, the JPEG `SOF0..SOF15` marker
  chain, the GIF logical screen descriptor, and all three WebP chunk variants (`VP8 `, `VP8L`,
  `VP8X`).

Parsing headers rather than decoding is what lets the CMS store width and height — and so render
`<img>` with explicit dimensions, avoiding layout shift — with **no imaging-library dependency**
and no licensing question.

## 5. Serving

`MediaController` handles `GET /media/{hash}/{fileName?}` and is `[AllowAnonymous]`. It lives in
`WebWayCMS.Core`, not `WebWayCMS.Admin`, so a **rendering-only host serves images too**.

- `Cache-Control: public, max-age=31536000, immutable` and an `ETag` of the hash; `If-None-Match`
  returns `304`. Content-addressing is what makes `immutable` correct.
- The trailing file name is cosmetic and ignored — the hash alone identifies the blob. It is
  sanitized to a conservative character set by `MediaUrl.SafeFileName`.
- `X-Content-Type-Options: nosniff` is already applied globally by the middleware pipeline.
- **No CSP change is needed**: `img-src 'self'` already covers a same-origin endpoint. This is a
  concrete advantage over referencing images on a third-party host.

### Routing

A literal `/media/...` path does **not** collide with the CMS route table: `MapControllers()` is
registered before the `{**slug}` dynamic catch-all, and a literal segment outranks a catch-all
regardless. `MediaController` additionally carries `[CmsRoute("/media", IsReserved = true)]`, which
seeds a reserved row so an editor cannot create a page at `/media` that would silently never
resolve. (`CmsRouteAttribute.IsReserved` was added for this; reserved rows occupy a pattern but are
never matched for dispatch.)

## 6. Upload

`MediaApiController` (`/api/media`, `[Authorize(Roles = "Admin")]`) is deliberately thin — all real
logic sits in `ImageValidator` and `MediaLibrary`, which live in coverage-gated projects.

| Route | Purpose |
|---|---|
| `POST /api/media/upload` | Multipart upload; validates, stores, returns hash + dimensions + URL |
| `GET /api/media/list` | Catalog page for the picker — metadata only, never bytes |
| `DELETE /api/media/{hash}` | Refused while any `ImageDTO` version still references the hash |

`[RequestSizeLimit]` is set explicitly. Nothing else in the solution configures a request-size or
multipart limit, so without it an upload endpoint would silently inherit Kestrel's 30 MB default.

Deletion checks **every** version, not just the published one: an editor can still restore an older
version, and that version's image must not have been deleted from underneath it.

## 7. The picker

`FormImagePicker` is registered by name (`[FormProperty(FormComponent = "ImagePicker")]`), following
`FormEntityPicker`. **No `EditorType` enum value was added** — `docs/form-control-system.md` notes
the enum change is optional, and a new control does not justify widening a shared enum.

`media-picker.js` (no jQuery; a file, not an inline script, because `script-src` has no
`'unsafe-inline'`) posts the file to the upload API and writes only the returned hash into a hidden
input. The surrounding admin form therefore stays `application/x-www-form-urlencoded` and never
carries bytes — which is exactly what keeps the same view model usable from MCP and seeding.

## 8. The widget

`ImageViewComponent` renders an image content item inside a content zone.
`ImageContentZoneConfiguration` references the **node**, not the hash, so alt text and caption stay
defined once on the content item and the reference is `@seed:{guid}`-resolvable from a seed file.

Two rules the component obeys, both load-bearing:

- `ImageNodeId` is `Guid?`, not `Guid`. A non-nullable value type in a widget configuration
  silently discards the *entire* saved configuration when the field is left empty — the editor
  writes `null`, deserialization throws, and `ContentZoneModel.DeserializePropertiesToConfigType`
  swallows it and returns an empty object.
- `InvokeAsync` takes **exactly one parameter** of the configuration type, nullable with a default.
  The zone renderer passes the deserialized configuration as the whole arguments object, which binds
  directly only for a single assignable parameter; any other shape is shredded into a property
  dictionary.

The widget also renders live inside the admin inline editor, so it degrades to empty content rather
than throwing when no image is selected yet.

## 9. Content seeding

A seed file can ship an image beside it using a `@media:{path}` token, resolved by
`SeedMediaResolver` — the same shape as `@seed:{guid}`:

```json
{ "id": "…", "contentType": "images",
  "fields": { "title": "Logo", "altText": "Our logo", "blobHash": "@media:images/logo.png" } }
```

The path resolves relative to the seed file's own directory, or as a manifest resource of the seed
resource's assembly (`ContentSeedSource` now carries its declaring `Assembly`). Paths may not escape
the seed file's directory. Seeded files go through the same `ImageValidator` checks as an admin
upload, and an unresolvable or invalid path is **not** saved and **not** hash-recorded, so the item
retries on the next boot — the same contract as an unresolved `@seed` reference.

The resolver is an *optional* dependency of `JsonContentSeeder`; a host wired without media support
leaves the tokens untouched.

## 10. MCP

`images` is visible to MCP generically through `IAdminHandlerRegistry` — no per-type tool code.
`MediaToolset` adds one read-only tool, `list_media`, because without it an agent has no way to
discover an existing hash to put in `blobHash`. **Bytes deliberately do not travel over MCP**;
uploading is an admin-UI or content-seed operation.

## 11. Configuration

```json
{ "Media": { "MaxUploadBytes": 10485760, "AllowedContentTypes": [] } }
```

`AllowedContentTypes` empty means "every format the validator can verify". Values are matched
against the **sniffed** type, never the client-declared header.

A host swaps the storage medium with `AddMediaStore<TStore>()` on `IWebWayCmsBuilder`. The default
is registered with `TryAdd`, so an explicit host choice wins regardless of registration order.

## 12. Known limitations

- **EXIF is not stripped.** GPS coordinates embedded in phone photos are served verbatim. This is
  the one hardening step deliberately deferred, because stripping metadata means re-encoding, which
  means an imaging library. Revisit when derivative generation lands.
- **No derivatives or `srcset`.** Originals are served at full size.
- **No orphan collection.** A blob whose referencing image versions were all hard-deleted stays in
  the library until deleted explicitly.
