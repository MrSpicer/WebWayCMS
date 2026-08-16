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
silently storing an empty row.

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

The tools carry accurate annotation hints for MCP clients: the 14 pure-read tools are `ReadOnly`, the
recoverable writes (`create_*`/`reorder_children`/`restore_*`) are marked non-destructive, and the
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
| `list_content_types` | Enumerate the registered `IAdminCrudHandler`s (including each type's `secondaryApiListKeys`) |
| `describe_content_type` | Field/metadata description for one type |
| `list_content` | List items of a type |
| `list_secondary_content` | List a type's additional named list, keyed by one of its `secondaryApiListKeys` |
| `get_content` | Fetch a single item |
| `create_content` | Create an item |
| `update_content` | Update an item |
| `delete_content` | Delete an item |
| `publish_content` | Publish the current draft of an item (gated on `SupportsPublishing`) |
| `unpublish_content` | Unpublish the published version of an item |
| `get_content_state` | Report an item's published/draft/current-version state |
| `list_registry` | The type's registry list, where it exposes one |
| `get_registry_properties` | Form property definitions for a registry entry |

**`VersionToolset`** — `list_versions` (keyed on `nodeId`), `get_version`, `restore_version` (one-step
DTO-level restore), `delete_version`.

**`ChildContentToolset`** — `list_children`, `get_child`, `get_child_version`, `create_child`,
`update_child`, `delete_child`, `reorder_children`, `list_child_versions`, `restore_child_version`,
`delete_child_version`.

The `publish_*`/`unpublish_*`/`restore_*` tools run with effective admin authority — the bearer token
is the security boundary — so an agent can publish. Their tool descriptions state this plainly. The
tool annotations reflect the same line: the read tools (`list_*`/`get_*`/`describe_content_type`/
`get_content_state`) are flagged `ReadOnly`; `create_*`/`reorder_children`/`restore_*` are flagged
non-destructive; and `update_*`, `delete_*`, `publish_content`, and `unpublish_content` keep the
`Destructive` default, with `publish_content` additionally `Idempotent`.

Because `list_content_types` reads the handler registrations, the content-type keys an agent sees
are exactly the admin URL segments: `pages`, `contentblocks`, `articles`, `contentzones`, `widgets`,
`pagetypes`, `formcomponents`, `cmsroutes`.

The `articles` type is asymmetric by design: `list_content("articles")` returns the individual
*articles* (the entity picker in `form-components.js` maps `'Article'` to that list), while the
article *lists* an agent creates live behind the secondary list
`list_secondary_content("articles", "articlelists")`. `list_content_types` advertises this via
`secondaryApiListKeys`, so an agent that just ran `create_content("articles")` knows to read the
lists back from the secondary list, not from `list_content`.

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
