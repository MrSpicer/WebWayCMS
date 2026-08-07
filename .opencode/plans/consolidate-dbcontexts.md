# Plan: Consolidate Multiple DbContexts into Single CmsDbContext

## Summary

Replace 5 separate DbContexts (`ApplicationDbContext`, `ArticleContext`, `ContentBlockContext`,
`ContentZoneContext`, `PageContext`) with a single `CmsDbContext : IdentityDbContext` that holds all
DbSets. Use extension methods on `ModelBuilder` to keep entity configuration organized by concern.

## Files to Create (2)

1. **`WebWayCMS.Data/Data/DbContexts/CmsDbContext.cs`** — Unified context with all DbSets. Inherits
   `IdentityDbContext`. `OnModelCreating` delegates to extension methods.

2. **`WebWayCMS.Data/Data/DesignTime/CmsDbContextFactory.cs`** — Single design-time factory replacing
   all 5 old factories. Preserves Identity `MaxLengthForKeys` config.

## Files to Modify (13)

### Source Files

3. **`WebWayCMS.Data/Data/DbContexts/ContentModelConfiguration.cs`** — Rework:
   - `ConfigureContent()` no longer takes `ownsTable` (no more dual ownership)
   - Add `ConfigureArticles()`, `ConfigureContentBlocks()`, `ConfigureContentZones()`,
     `ConfigurePages()` extension methods on `ModelBuilder`
   - Keep `ConfigureContentLink<T>()` helper

4. **`WebWayCMS.Data/Data/Services/PageService.cs`** — Inject `CmsDbContext` instead of `PageContext`

5. **`WebWayCMS.Data/Data/Services/ContentZoneService.cs`** — Inject `CmsDbContext` instead of
   `ContentZoneContext`

6. **`WebWayCMS/ServiceCollectionExtensions.cs`** — Replace 5 `AddDbContext<>()` calls with 1
   `AddDbContext<CmsDbContext>`, update `AddEntityFrameworkStores<CmsDbContext>()`, update
   `IContentService<T>` factories

7. **`WebWayCMS/CMSExtensions.cs`** — Single `Migrate<CmsDbContext>()` call instead of 5; remove
   ordering comment

### Test Files

8. **`tests/WebWayCMS.Data.Tests/TestContexts.cs`** — Replace 5 factory methods with 1 `Cms(string db)`
   returning `CmsDbContext`

9. **`tests/WebWayCMS.Data.Tests/DbContextTests.cs`** — Update to use `CmsDbContext`; merge
   `ArticleContext_ConfiguresArticlesAndArticleLists` and `ApplicationDbContext_CanBeConstructed`
   into tests exercising the unified context

10. **`tests/WebWayCMS.Data.Tests/ContentServiceTests.cs`** — Change `NewContext()` to return
    `CmsDbContext`

11. **`tests/WebWayCMS.Data.Tests/PageServiceTests.cs`** — Change `NewContext()` to return
    `CmsDbContext`

12. **`tests/WebWayCMS.Data.Tests/ContentZoneServiceTests.cs`** — Change `NewContext()` to return
    `CmsDbContext`

13. **`tests/WebWayCMS.Host.Tests/CMSExtensionsTests.cs`** — Register 1 context instead of 5

14. **`tests/WebWayCMS.Host.Tests/ServiceCollectionExtensionsTests.cs`** — Assert on `CmsDbContext`
    instead of `PageContext`

### Scripts / Docs

15. **`scripts/RebuildEFMigrations.sh`** — Single `dotnet ef migrations add` for `CmsDbContext`

## Files to Delete (17)

### Old DbContexts (5)
16. `WebWayCMS.Data/Data/DbContexts/ApplicationDbContext.cs`
17. `WebWayCMS.Data/Data/DbContexts/ArticleContext.cs`
18. `WebWayCMS.Data/Data/DbContexts/ContentBlockContext.cs`
19. `WebWayCMS.Data/Data/DbContexts/ContentZoneContext.cs`
20. `WebWayCMS.Data/Data/DbContexts/PageContext.cs`

### Old Design-Time Factories (5)
21. `WebWayCMS.Data/Data/DesignTime/ApplicationDbContextFactory.cs`
22. `WebWayCMS.Data/Data/DesignTime/ArticleContextFactory.cs`
23. `WebWayCMS.Data/Data/DesignTime/ContentBlockContextFactory.cs`
24. `WebWayCMS.Data/Data/DesignTime/ContentZoneContextFactory.cs`
25. `WebWayCMS.Data/Data/DesignTime/PageContextFactory.cs`

### Old Migration Directories (5)
26. `WebWayCMS.Data/Migrations/Identity/` (entire directory)
27. `WebWayCMS.Data/Migrations/Article/` (entire directory)
28. `WebWayCMS.Data/Migrations/ContentBlock/` (entire directory)
29. `WebWayCMS.Data/Migrations/ContentZone/` (entire directory)
30. `WebWayCMS.Data/Migrations/Page/` (entire directory)

### Docs
31. `docs/architecture/01-data-tier.md` — Update all context references
32. `docs/architecture/07-cms-bootstrap.md` — Update all context references
33. `docs/architecture/08-identity-auth.md` — Update context reference
34. `docs/content-system.md` — Update context reference
35. `docs/architecture/README.md` — Update context references
36. `docs/page-system.md` — Update context reference

## Verification

1. `dotnet build` — must succeed with 0 errors
2. `./scripts/RebuildEFMigrations.sh` — must generate fresh migration under `Migrations/`
3. `./scripts/RunTests.sh` — must pass with 100% coverage gate
