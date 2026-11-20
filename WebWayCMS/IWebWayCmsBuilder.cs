using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mapping;
using WebWayCMS.Startup;

namespace WebWayCMS;

/// <summary>
/// Fluent builder for extending a WebWayCMS host with its own EF-backed content types, mapping
/// profiles, and migrations — entirely from the host's own project, with no CMS source changes.
/// </summary>
public interface IWebWayCmsBuilder
{
    /// <summary>The service collection being configured. Escape hatch for advanced registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers the assembly's entity configurations into the EF model, enrolls it in the four
    /// content-type seeders, and adds it as an MVC application part (controllers, view components,
    /// compiled Razor views). One call covers all three.
    /// </summary>
    IWebWayCmsBuilder AddApplicationAssembly(Assembly assembly);

    /// <summary>Registers a custom model extension type into the EF model.</summary>
    IWebWayCmsBuilder AddModelConfiguration<TConfig>() where TConfig : class, ICmsModelExtension;

    /// <summary>Registers a model extension instance into the EF model.</summary>
    IWebWayCmsBuilder AddModelConfiguration(ICmsModelExtension extension);

    /// <summary>Registers an inline model contribution into the EF model.</summary>
    IWebWayCmsBuilder ConfigureModel(Action<ModelBuilder> configure);

    /// <summary>
    /// Registers the generic content store for a host-defined content type, and the content type itself: it joins
    /// <c>IContentTypeRegistry</c> and gets a content type registration (<c>/wadmin/contenttypes</c>) at boot.
    /// <paramref name="displayName"/> / <paramref name="pluralDisplayName"/> are the registration's starting
    /// names; when omitted they come from the type's admin handler, else from <typeparamref name="T"/>'s name.
    /// </summary>
    IWebWayCmsBuilder AddContentType<T>(string contentTypeKey, string? displayName = null, string? pluralDisplayName = null)
        where T : class, IVersionedContent;

    /// <summary>Contributes a mapping profile to the shared <see cref="IMapper"/> singleton.</summary>
    IWebWayCmsBuilder AddMappingProfile(Profile profile);

    /// <summary>
    /// Registers a host-owned migrations-only context (see <see cref="CmsExtensionDbContext{TSelf}"/>)
    /// with Npgsql and the given history table, and enrolls it in the startup migration runner.
    /// </summary>
    IWebWayCmsBuilder AddMigrationsContext<TContext>(string historyTable) where TContext : DbContext;

    /// <summary>
    /// Registers a JSON content seed file to be applied at startup (path resolved against the host's
    /// content root when not rooted).
    /// </summary>
    IWebWayCmsBuilder AddContentSeedFile(string path);

    /// <summary>
    /// Registers an assembly whose embedded <c>*.contentseed.json</c> resources are applied at startup.
    /// </summary>
    IWebWayCmsBuilder AddContentSeedAssembly(Assembly assembly);

    /// <summary>
    /// Replaces the storage medium for media bytes. The CMS default stores them in Postgres; a host
    /// can swap in a filesystem or cloud implementation without touching the media catalog, which
    /// always stays in the database.
    /// </summary>
    IWebWayCmsBuilder AddMediaStore<TStore>() where TStore : class, IMediaBlobStore;
}
