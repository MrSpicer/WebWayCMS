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
the rich-text sanitization that `AdminCrudModel<T>.SaveUpsertAsync` performs, since MCP writes go
through the same save choke point.

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
  them via `AddMcpServer().WithHttpTransport().WithTools(tools)`

Two SDK options are set deliberately:
- `TransformSchemaNode` declares the free-form `JsonElement` "fields" parameters as `object` in the
  tool schema, so clients send a real JSON object rather than a JSON string
- `ReferenceHandler.IgnoreCycles`, because tool results are EF-backed DTO graphs that contain cycles
  (a content zone's items reference the zone back)

**Pipeline** — `MapWebWayCmsMcp(app)` no-ops when disabled; otherwise `app.MapMcp(options.Path)`
with the API-key endpoint filter attached.

Both extension classes are `[ExcludeFromCodeCoverage]` — they are SDK/transport wiring validated by
running the app. The toolset logic is unit-tested to the 100% gate.

---

## 5. Tool Surface

**`ContentToolset`** — top-level content and registries:

| Tool | Purpose |
|---|---|
| `list_content_types` | Enumerate the registered `IAdminCrudHandler`s |
| `describe_content_type` | Field/metadata description for one type |
| `list_content` | List items of a type |
| `get_content` | Fetch a single item |
| `create_content` | Create an item |
| `update_content` | Update an item |
| `delete_content` | Delete an item |
| `list_registry` | The type's registry list, where it exposes one |
| `get_registry_properties` | Form property definitions for a registry entry |

**`VersionToolset`** — `list_versions`, `get_version`, `restore_version`, `delete_version`.

**`ChildContentToolset`** — `list_children`, `get_child`, `create_child`, `update_child`,
`delete_child`, `reorder_children`, `list_child_versions`, `restore_child_version`,
`delete_child_version`.

Because `list_content_types` reads the handler registrations, the content-type keys an agent sees
are exactly the admin URL segments: `pages`, `contentblocks`, `articles`, `contentzones`, `widgets`,
`pagetypes`, `cmsroutes`.

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

The integration host in this repo enables MCP against `http://localhost:45847/mcp` — see
`WebWayCMS.TestHost/appsettings.json`. That is a throwaway stack with a checked-in key; do not copy
that pattern into a real deployment.

---

*See also:* [06-admin-crud-framework](06-admin-crud-framework.md) for the handler contract the tools
drive, and [13-security](13-security.md) for the sanitization that applies to MCP writes.
