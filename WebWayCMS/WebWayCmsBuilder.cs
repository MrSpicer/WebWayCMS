using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
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

    /// <summary>Registers the generic content store for a host-defined content type.</summary>
    IWebWayCmsBuilder AddContentType<T>(string contentTypeKey) where T : class, IVersionedContent;

    /// <summary>Contributes a mapping profile to the shared <see cref="IMapper"/> singleton.</summary>
    IWebWayCmsBuilder AddMappingProfile(Profile profile);

    /// <summary>
    /// Registers a host-owned migrations-only context (see <see cref="CmsExtensionDbContext{TSelf}"/>)
    /// with Npgsql and the given history table, and enrolls it in the startup migration runner.
    /// </summary>
    IWebWayCmsBuilder AddMigrationsContext<TContext>(string historyTable) where TContext : DbContext;
}

internal sealed class WebWayCmsBuilder : IWebWayCmsBuilder
{
    private readonly IConfiguration _configuration;
    private readonly List<Assembly> _assemblies = new();
    private readonly List<Type> _migrationContextTypes = new();
    private readonly List<Profile> _profiles = new();

    public WebWayCmsBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public IServiceCollection Services { get; }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    internal IReadOnlyList<Type> MigrationContextTypes => _migrationContextTypes;

    internal IReadOnlyList<Profile> Profiles => _profiles;

    public IWebWayCmsBuilder AddApplicationAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (_assemblies.Contains(assembly))
            return this;

        _assemblies.Add(assembly);
        Services.AddSingleton<ICmsModelExtension>(new AssemblyModelExtension(assembly));
        return this;
    }

    public IWebWayCmsBuilder AddModelConfiguration<TConfig>() where TConfig : class, ICmsModelExtension
    {
        Services.AddSingleton<ICmsModelExtension, TConfig>();
        return this;
    }

    public IWebWayCmsBuilder AddModelConfiguration(ICmsModelExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        Services.AddSingleton<ICmsModelExtension>(extension);
        return this;
    }

    public IWebWayCmsBuilder ConfigureModel(Action<ModelBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddSingleton<ICmsModelExtension>(new DelegateModelExtension(configure));
        return this;
    }

    public IWebWayCmsBuilder AddContentType<T>(string contentTypeKey) where T : class, IVersionedContent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeKey);
        CmsRenderingRegistration.AddContentStore<T>(Services, contentTypeKey);
        return this;
    }

    public IWebWayCmsBuilder AddMappingProfile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles.Add(profile);
        return this;
    }

    public IWebWayCmsBuilder AddMigrationsContext<TContext>(string historyTable) where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTable);
        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        Services.AddDbContext<TContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsHistoryTable(historyTable)));
        _migrationContextTypes.Add(typeof(TContext));
        return this;
    }

    internal void RegisterCatalogs()
    {
        Services.AddSingleton(new CmsAssemblyCatalog(_assemblies));
        Services.AddSingleton(new CmsMigrationsContextCatalog(_migrationContextTypes));
    }
}
