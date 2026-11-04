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

internal sealed class WebWayCmsBuilder : IWebWayCmsBuilder
{
    private readonly IConfiguration _configuration;
    private readonly List<Assembly> _assemblies = new();
    private readonly List<Type> _migrationContextTypes = new();
    private readonly List<Profile> _profiles = new();
    private readonly List<string> _contentSeedFiles = new();
    private readonly List<Assembly> _contentSeedAssemblies = new();

    public WebWayCmsBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public IServiceCollection Services { get; }

    internal IReadOnlyList<Assembly> Assemblies => _assemblies;

    internal IReadOnlyList<Type> MigrationContextTypes => _migrationContextTypes;

    internal IReadOnlyList<Profile> Profiles => _profiles;

    internal IReadOnlyList<string> ContentSeedFiles => _contentSeedFiles;

    internal IReadOnlyList<Assembly> ContentSeedAssemblies => _contentSeedAssemblies;

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

    public IWebWayCmsBuilder AddMediaStore<TStore>() where TStore : class, IMediaBlobStore
    {
        // The default is registered with TryAdd, so an explicit host choice registered here wins
        // regardless of the order the two registrations happen in.
        Services.AddScoped<IMediaBlobStore, TStore>();
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

    public IWebWayCmsBuilder AddContentSeedFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _contentSeedFiles.Add(path);
        return this;
    }

    public IWebWayCmsBuilder AddContentSeedAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (_contentSeedAssemblies.Contains(assembly))
            return this;

        _contentSeedAssemblies.Add(assembly);
        return this;
    }

    internal void RegisterCatalogs()
    {
        Services.AddSingleton(new CmsAssemblyCatalog(_assemblies));
        Services.AddSingleton(new CmsMigrationsContextCatalog(_migrationContextTypes));
        Services.AddSingleton(new CmsContentSeedCatalog(_contentSeedFiles, _contentSeedAssemblies));
    }
}
