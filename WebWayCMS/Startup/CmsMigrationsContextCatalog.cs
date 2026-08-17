namespace WebWayCMS.Startup;

/// <summary>
/// Holds the host migration-context types contributed via
/// <c>IWebWayCmsBuilder.AddMigrationsContext&lt;TContext&gt;</c>, in registration order. Registered
/// as a singleton so <see cref="CmsMigrationRunner"/> can migrate them after
/// <c>CmsDbContext</c> (CMS-first ordering is what makes a host table's FK to
/// <c>ContentVersions</c> resolvable).
/// </summary>
internal sealed class CmsMigrationsContextCatalog
{
    public CmsMigrationsContextCatalog(IReadOnlyList<Type> contexts) => Contexts = contexts;

    public IReadOnlyList<Type> Contexts { get; }
}
