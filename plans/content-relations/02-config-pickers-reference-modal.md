# Phase 2: Config Pickers as References + Picker/Modal UX

**Goal:**

- Widget and page configuration pickers become reference relations owned by the zone-item or page
  version. The id leaves the config JSON.
- The EntityPicker supports many values.
- New / Edit buttons open the target's form in a modal with its own save.

## 0. Test impact review

Get approval for the existing tests this phase changes. Expected:

- `WidgetConfigurationJson` tests (relation keys are stripped from the stored JSON);
- `ContentZoneModel` render/deserialize tests (configs are hydrated from relations);
- `CMSRouteTransformer` page-config tests;
- `FormEntityPicker` view-component tests;
- the Image, Article and ContentBlock widget view-component tests that build configs with ids.

## 1. Declare the core config pickers

Replace `[FormProperty(FormComponent = "EntityPicker", EntityType = …)]` with
`[ContentRelation("<key>")]` (plus a `[FormProperty]` for label and order) on:

- `ImageContentZoneConfiguration` (image): `OnTargetDelete = Restrict`, so an image in use can't be
  deleted;
- `ArticleContentZoneConfiguration` (`ArticleId`, `ArticleListId`): `Detach`;
- `ContentBlockContentZoneConfiguration`: `Restrict`.

Relation names for config properties are prefixed `config.` (e.g. `config.ImageId`), so they can't
collide with a type's own upsert relations. `RelationProperties` takes a name prefix.

The registry (`ContentRelationRegistry`) also scans:

- widget configuration types (`IWidgetRegistry`), keyed under source type `contentzoneitems` (the zone
  item store's content type key, `CmsRenderingRegistration.cs:85`) and qualified by component name;
- page configuration types (`IPageControllerRegistry`), keyed under `pages`.

## 2. Writes: zone items and pages

- **Zone items** (`ContentZoneModel` and `ContentZoneChildHandler`, wherever
  `WidgetConfigurationJson.Sanitize` runs today):
  - Add `WidgetConfigurationJson.ExtractRelations(registry, componentName, json)`, which returns
    `(string StrippedJson, IReadOnlyList<RelationValue> Relations)`. It reads the config type's
    `[ContentRelation]` keys from the JSON and removes them. Unreadable JSON fails closed to `{}` with
    no relations, as `Sanitize` does.
  - The item DTO's version gets `SetRelation(s)` for every declared config name. Undeclared names are
    left alone and carry forward.
  - The binder's existence check runs here too, returning a 400 with the field name from the zone API
    and a field error from the child handler.
- **Pages:** `PageModel.SaveUpsertCoreAsync` does the same for `ConfigurationJson` against the page
  controller's configuration type.

## 3. Reads: hydrate before render

- `ContentZoneModel.DeserializePropertiesToConfigType` (≈ line 520): after deserializing, call
  `RelationProperties.Write(config, item.Version.Relations, prefix: "config.")`.
- `CMSRouteTransformer.DeserializeConfig` (`CMSRouteTransformer.cs:136`): the same for the page
  config, from `page.Version.Relations`.
- Read-context correctness comes for free, because the relations belong to the version being rendered.
- Every place that serializes a config back for the zone editor or the config form (the
  `POST /wadmin/widgets/registry/{name}/form` endpoint, the MCP `get_child` view model) hydrates first,
  so the form shows the picked values.

## 4. Multi-select EntityPicker

- **`FormEntityPicker`** (`WebWayCMS.Presentation/ViewComponents/Forms/FormEntityPicker.cs` and
  `Views/Shared/Components/FormEntityPicker/adminwrite.default.cshtml`):
  - when `field.IsMultiple`, render a list of chosen entries (title + remove button), each backed by a
    hidden input `name="{Field}[i]"` (model-bound forms);
  - on JSON-bound config forms, a single `data-prop` element holds a JSON array (the area-14 rule:
    exactly one `data-prop` element per field);
  - add an "Add" select fed by the `data-endpoint` list;
  - the read view (`adminread.default`) lists the titles.
- **`form-components.js`** (`WebWayCMS.Admin/Views/Shared/form-components.js`): add/remove/reorder for
  multi entries. Keep it vanilla JS.
- **Config JSON serializer:** emit an array for multi fields (there is no `IEnumerable` config field
  today, so add a test fixture).

## 5. The reference modal

- **Server:**
  - `AdminContentController` `Create`/`Edit` GET accept `?modal=1`. They set
    `ViewData[AdminModal.ViewDataKey] = true`, using a new static `AdminModal` in
    `WebWayCMS.Admin/Controllers/`.
  - `EditPost` with `modal=1` returns:
    - success: `200 { id, title }` JSON (the title comes from the handler's `GetApiListAsync` entry,
      or the saved view model's `Title`);
    - validation failure: the form re-rendered as a modal partial with status `422`.
  - Antiforgery is unchanged: the modal form carries its own token.
- **Layout:** `Views/Shared/_AdminLayout.cshtml` branches on `AdminModal.IsModal(ViewData)`. The
  minimal branch renders only `<main>` with `RenderBody()` and the page's scripts, without the navbar.
  No `_ViewStart` changes are needed.
- **Client:** a new `WebWayCMS.Admin/Views/Shared/relation-modal.js` source (added to
  `CopyViewScripts` and included by `_AdminLayout`):
  - **New** opens a Bulma modal with an iframe on `/wadmin/{target}/create?modal=1`.
  - **Edit** opens it on `/wadmin/{target}/edit/{id}?modal=1`.
  - The iframe page posts its form with `fetch`. On `{ id, title }` it messages the parent through
    `postMessage` with an origin check. The picker adds or updates the entry and closes the modal. A
    `422` re-renders the iframe.
  - An iframe keeps the target form's own scripts (rich text, pickers, nested modals) working unchanged.
    It needs `frame-ancestors 'self'` for `/wadmin/*?modal=1` responses: extend
    `CspPolicyBuilder.BuildForPreview`'s pattern with a modal variant
    (`X-Frame-Options: SAMEORIGIN`).
  - The source form is not saved by the modal.
- **Buttons:** the picker shows New / Edit only when the current user has write access to the target
  type. The view model gets `CanCreate`/`CanEdit` from the handler's `WriteRoles`.
- **Owned relations** (used by phase 3): no "pick existing" select, only New / Edit / Remove. Remove
  detaches; the orphaned owned target is deleted on the source's next publish (phase 3).

## 6. Seed and MCP

- Seed files and MCP still send config ids inside `componentPropertiesJson`. `ExtractRelations` lifts
  them out, so `@seed:` tokens inside config JSON keep working (they are resolved before the save).
- `get_child` returns the hydrated config, so ids are visible to agents.
- `describe` for widget registry properties (`get_registry_properties`) shows the relation metadata.

## 7. Migrations, docs

- `RebuildEFMigrations.sh` (no schema change is expected beyond phase 1; run it to confirm the model is
  unchanged).
- **Docs:**
  - `CLAUDE.md`: Media (the Image widget stores a relation, not an id in JSON), Widget config 4000-char
    note, Security (config sanitize + extract);
  - `docs/widget-system.md`, `docs/form-control-system.md` (the multi picker, the modal),
    `13-security.md` (the CSP framing exception for modal responses);
  - doc 17 §7 config row and §9 reference UX: implemented.

## Verification

- Unit coverage 100% (Core, Presentation, Admin-facing Core handlers, Routing, Mcp).
- Integration host:
  1. Place an Image widget, pick an image, and publish the page. The image renders. The stored
     `ComponentPropertiesJson` has no image id (check with `get_child`, or the DB via `docker exec psql`).
  2. Delete that image. It is refused with "used by 1 item". Remove the widget, publish, and delete the
     image again: it succeeds.
  3. Restore the widget's earlier version. It references the deleted image and renders nothing,
     without an error.
  4. In the Article form, click **New** next to Article List. The modal opens, and a validation error
     re-renders inside it. Save: the new list appears selected in the picker. The Article form itself
     is still unsaved.
  5. A multi-select fixture (a TestHost page config or a test widget with `List<Guid>`) adds, removes
     and reorders entries, and the stored relation ordinals match.
- Playwright: a modal round-trip test in the TestHost `e2e/` suite (added in phase 6, or here if the
  suite is touched).
