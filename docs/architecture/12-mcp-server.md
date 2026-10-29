# Area 12: MCP Server

**Namespaces:**
- `WebWayCMS.Mcp` — `McpOptions`, `McpServiceCollectionExtensions`, `McpApplicationBuilderExtensions`, `McpApiKeyEndpointFilter`, `ContentToolset`, `VersionToolset`, `ChildContentToolset`, `McpToolHelpers`

**Depends on:** Admin CRUD Framework (`IAdminHandlerRegistry`, `IAdminCrudHandler`), Form Generation Metadata, the official `ModelContextProtocol.AspNetCore` SDK
**Consumed by:** MCP clients (Claude Code, Claude Desktop, any MCP-capable agent)

---

## 1. What It Is

`WebWayCMS.Mcp` exposes the CMS admin feature set — content CRUD, child entities, version history,
and the registries — to AI agents over the Model Context Protocol.

The design point is that **there is no per-type tool code**. The toolsets resolve
`IAdminHandlerRegistry` and drive exactly the same `IAdminCrudHandler` methods the admin UI drives.
Every content type registered today, and every one added later, is covered automatically — including
the rich-text sanitization and the required-field validation that `AdminCrudModel<T>.SaveUpsertAsync`
performs, since MCP writes go through the same save choke point. A field `describe_content_type`
advertises as `required` is actually enforced on the MCP write path (see [Area 13](13-security.md)),
so `create_content("contentblocks", fields={})` fails with an `errorField`/`errorMessage` instead of
silently storing an empty row. The same choke point runs the relation binder, so a relation field naming a
missing node or the wrong type (e.g. a bogus `articleListId`) comes back as a field error, not a 500.

---

## 2. Configuration

Bound from the `"Mcp"` section into `McpOptions`:

| Key | Type | Default | Description |
|---|---|---|---|
| `Mcp:Enabled` | `bool` | `false` | Whether the endpoint is mapped. **Opt-in.** |
| `Mcp:ApiKey` | `string?` | `null` | Static bearer token required on every request |
| `Mcp:Path` | `string` | `"/mcp"` | Route the server is mapped to |

```jsonc
"Mcp": {
  "Enabled": true,
  "ApiKey": "<generated-key>",
  "Path": "/mcp"
}
```

Supply `ApiKey` via user-secrets or environment variables, never source control.

---

## 3. Security Model

**The API key is the only security boundary.** The MCP endpoint executes with effective admin
authority — it is not behind `[Authorize(Roles = "Admin")]`, because an agent has no Identity
cookie. Anyone holding the token can do anything an admin can do.

Three deliberate behaviours follow from that:

1. **Fail loud, not open.** `MapWebWayCmsMcp` throws `InvalidOperationException` at startup if
   `Enabled` is true and `ApiKey` is empty, rather than mapping an endpoint that only ever 401s.
2. **Constant-time comparison.** `McpApiKeyEndpointFilter` compares the presented token with
   `CryptographicOperations.FixedTimeEquals`, so the check does not leak the key through timing.
   The `Bearer ` prefix is optional and matched case-insensitively.
3. **Admin mode only.** `MapWebWayCmsMcp()` is called from `ConfigureAdminPipeline`, never from the
   rendering pipeline — a rendering-only host cannot expose MCP even if `Mcp:Enabled` is true in its
   configuration. See [Area 11](11-deployment-modes.md).

Treat the key like a root credential: rotate it, scope network access to the endpoint where you can,
and prefer not to enable MCP on an internet-facing host at all.

---

## 4. Wiring

**DI** — `AddWebWayCmsMcp(services, configuration)`, called from `AddWebWayCmsAdmin`:
- binds `McpOptions`
- registers `ContentToolset`, `VersionToolset`, `ChildContentToolset` as **scoped**, so each tool
  call resolves fresh scoped admin handlers
- builds `McpServerTool`s by reflecting over each toolset's `[McpServerTool]` methods and registers
  them via `AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools(tools)`

Three SDK options are set deliberately:
- `SessionMode = Stateless` — pinned to match the SDK 2.x default (1.4 defaulted to stateful; 2.0
  flipped it), so a future default-shift can't move it silently. Stateless is the right posture here:
  the toolsets never make server-to-client requests (no sampling/elicitation/roots), and each tool
  call resolving from the per-HTTP-request scope keeps `DbContext` lifetimes short.
- `TransformSchemaNode` declares the free-form `JsonElement` "fields" parameters as `object` in the
  tool schema, so clients send a real JSON object rather than a JSON string
- `ReferenceHandler.IgnoreCycles`, because tool results are EF-backed DTO graphs that contain cycles
  (a content zone's items reference the zone back)

Because `SessionMode` is `Stateless`, the endpoint is **Streamable-HTTP only**: there is no
`Mcp-Session-Id`, and the legacy `/sse` and `/message` routes are not mapped (SDK 2.x defaults
`EnableLegacySse` to `false`). A client must be configured `"type": "http"` (see §6), not
`"type": "sse"`, or it will 404.

One consequence worth knowing when debugging: a client that connected to an *older, stateful* server
may still be holding an `Mcp-Session-Id`. Against the stateless endpoint that session id is rejected
(`400: Mcp-Session-Id header is not supported in stateless mode`). The fix is to reconnect — after
`/mcp` reconnects without the stale header, a real client works normally. This is stale client state,
not a server bug.

The tools carry accurate annotation hints for MCP clients: the 16 pure-read tools are `ReadOnly`, the
recoverable writes (`create_*`/`reorder_children`/`move_child`/`restore_*`/`revert_page_to_entry`) are marked non-destructive, and the
overwriting or irreversible `update_*`/`delete_*`/`publish_content`/`unpublish_content` tools keep the
`Destructive` default (with `publish_content` additionally `Idempotent`).

**Pipeline** — `MapWebWayCmsMcp(app)` no-ops when disabled; otherwise `app.MapMcp(options.Path)`
with the API-key endpoint filter attached.

Both extension classes are `[ExcludeFromCodeCoverage]` — they are SDK/transport wiring validated by
running the app. The toolset logic is unit-tested to the 100% gate.

---

## 5. Tool Surface

**`ContentToolset`** — top-level content and registries:

| Tool | Purpose |
|---|---|
| `list_content_types` | Enumerate the registered `IAdminCrudHandler`s (including each type's `secondaryApiListKeys` and `childTypes`: the types a containment places under it) |
| `describe_content_type` | Field/metadata description for one type. A `[ContentRelation]` field carries `relation: { kind, target, many, required }` (kind `containment`/`reference`/`owned`); its value is the target's node id, or an array of ids when `many` |
| `list_content` | List items of a type |
| `list_secondary_content` | List a type's additional named list, keyed by one of its `secondaryApiListKeys` |
| `get_content` | Fetch a single item |
| `create_content` | Create an item |
| `update_content` | Update an item |
| `delete_content` | Delete an item, with everything it contains (an article list's articles; a page's whole subtree of child pages with their zones and routes): returns `{ deleted, deletedDescendants }` (zones and zone items are not counted); a delete the relation engine refuses (the item is still referenced) throws with the reason |
| `publish_content` | Publish the current draft of an item (gated on `SupportsPublishing`) |
| `unpublish_content` | Unpublish the published version of an item |
| `get_content_state` | Report an item's published/draft/current-version state |
| `list_registry` | The type's registry list, where it exposes one |
| `get_registry_properties` | Form property definitions for a registry entry |

**`VersionToolset`** — `list_versions` (keyed on `nodeId`), `get_version`, `restore_version` (one-step
DTO-level restore), `delete_version`. `delete_version` and `delete_child_version` return
`deleted: false` when the latest version was asked for, and throw with the reason when the version is
part of a page history entry (delete the entry first).

**`PageHistoryToolset`** — a page's page-set history (an entry after every change to the page or its zone
items and after every publish; see [04 → History in the Zone Editor](04-content-zone-framework.md#8b-history-in-the-zone-editor)):

| Tool | Purpose |
|---|---|
| `list_page_history(pageNodeId, skip?, take?)` | Entries, newest first — one per change (kind Draft, with a note) and per publish (kind Published) — with change counts. Paged: `take` default 50, max 200; returns `totalCount`/`hasOlder` (`ReadOnly`) |
| `get_page_history_entry(pageNodeId, entryId, compareWithCurrent=false)` | The entry's items and their change vs. the previous entry; with `compareWithCurrent`, how the current drafts changed since the entry plus the `stateToken` a revert needs (`ReadOnly`) |
| `revert_page_to_entry(pageNodeId, entryId, stateToken)` | Revert the page and every zone item to the entry as drafts (non-destructive; refused when the page changed since the token) — publish with `publish_content("pages", …)` |
| `delete_page_history_entry(pageNodeId, entryId)` | Delete the entry record (its versions are kept) |

**`ChildContentToolset`** — generic over **containment** (a child is ordinary content of its own type,
placed under a parent by a `[ContentRelation(Kind = Containment)]`; there is no child-handler contract):

| Tool | Arguments | Purpose |
|---|---|---|
| `list_children` | `parentType`, `parentKey`, `childType`, `relation?` | The parent's children as `{ id, title, state, ordinal }` (current drafts, by position) |
| `create_child` | `parentType`, `parentKey`, `childType`, `fields`, `relation?` | `create_content` on the child type with its containment set to the parent (the argument wins over `fields`) |
| `reorder_children` | `parentType`, `parentKey`, `childType`, `orderedIds`, `relation?` | Ordered containments only; every child once |
| `move_child` | `parentType`, `targetParentKey`, `childType`, `childId`, `position?`, `expectedVersionNumber?`, `relation?` | Re-parent (a new draft), at `position` for an ordered containment; refused with the stale message when the child changed since `expectedVersionNumber` |
| `get_child`, `update_child`, `delete_child` | `childType`, `id` (+ `fields`) | The child type's ordinary handler operations |
| `list_child_versions`, `get_child_version`, `restore_child_version`, `delete_child_version` | `childType`, `nodeId` / `versionId` (+ `expectedVersionNumber?` on restore) | The child type's version history |

`parentKey`/`targetParentKey` are node ids. `relation` (the child's relation field name, case-insensitive) is
needed only when the child type has several containments to the parent type; otherwise the one containment
is used, and an unrelated pair is refused. This was a hard break of the old `contentType` + `childType: "items"`
+ string `parentKey` arguments (pre-release). Examples: `articlelists` + `articles`, `pages` + `pages`
(child pages), `contentzones` + `contentzoneitems` (a zone's items).

Zone items (`contentzoneitems`, `ContentZoneItemModel`): `create_child`/`update_child` results carry the item's
`nodeId`. `update_child` merges over the stored item, whose `expectedVersionNumber` is its current version; an
agent that passes the version it read gets `StaleVersionMessage` instead of overwriting a newer edit, and an
update never moves the item (use `move_child`). `update_child` and `restore_child_version` on a container (e.g. a
Layout) whose view changes move its nested widgets into the new view's zones, like the zone editor (see
[04 → Changing a container's view](04-content-zone-framework.md#changing-a-containers-view)).
`list_child_versions` on a container returns its combined timeline: its versions plus every nested widget's,
newest first, each row with its `nodeId` and `widgetLabel` (`isCombined: true`). `restore_child_version` is the
same one-step restore as the admin history page (`IAdminCrudHandler.RestoreVersionAsync(versionId, expected)`):
the widget, configuration, settings, active state, zone and position come back, refused (a failed save result)
when the old zone no longer exists on the page; an unknown version throws. `move_child` moves a zone item only
between zones of one page (never into its own nested zones; `position` default last). `delete_child` on a zone
item **removes** it (an inactive version), so a restore or page revert brings it back.

`reorder_children` and `move_child` surface a handler failure as a thrown `McpException` carrying the
handler's message, never as a bare `false` result.

Child writes on zone items follow the zone draft rule: in a zone that belongs to a page they
create **drafts** that go live only when the page is published (`publish_content("pages", nodeId)`
publishes the page and every pending zone item under it); in a global zone they go live immediately.

The `publish_*`/`unpublish_*`/`restore_*` tools run with effective admin authority — the bearer token
is the security boundary — so an agent can publish. Their tool descriptions state this plainly. The
tool annotations reflect the same line: the read tools (`list_*`/`get_*`/`describe_content_type`/
`get_content_state`) are flagged `ReadOnly`; `create_*`/`reorder_children`/`move_child`/`restore_*` are flagged
non-destructive; and `update_*`, `delete_*`, `publish_content`, and `unpublish_content` keep the
`Destructive` default, with `publish_content` additionally `Idempotent`.

Because `list_content_types` reads the handler registrations, the content-type keys an agent sees
are exactly the admin URL segments: `pages`, `contentblocks`, `images`, `articlelists`, `articles`,
`contentzones`, `contentzoneitems`, `widgets`, `pagetypes`, `formcomponents`, `cmsroutes`, `routes`.

Articles and article lists are two ordinary top-level types: create a list with
`create_content("articlelists", …)`, then an article with `create_content("articles", …)` whose `fields`
include `articleListId` (the list's node id — required; an empty or unknown id is rejected on that field),
and `publish_content` each, or create the article with `create_child("articlelists", listId, "articles", …)`.
A page's parent is its `parentNodeId` field (a containment; empty for a root page): `update_content` with a new
`parentNodeId` moves it as a draft, and its publish re-derives its URL and its live descendants'.

---

## 6. Connecting a Client

Once the server is enabled, add to `.mcp.json`:

```json
{
  "mcpServers": {
    "webwaycms": {
      "type": "http",
      "url": "https://localhost:7046/mcp",
      "headers": { "Authorization": "Bearer <key>" }
    }
  }
}
```

The integration host ([separate repo](https://github.com/MrSpicer/WebWayCMS.TestHost))
enables MCP against `http://localhost:45847/mcp` — see its
`appsettings.json`. That is a throwaway stack with a checked-in key; do not copy that pattern into a
real deployment.

---

*See also:* [06-admin-crud-framework](06-admin-crud-framework.md) for the handler contract the tools
drive, and [13-security](13-security.md) for the sanitization that applies to MCP writes.
