using Microsoft.EntityFrameworkCore;

namespace WebWayCMS.Data.DbContexts;

/// <summary>
/// A host-contributed piece of the EF Core model. Implementations are injected into
/// <see cref="CmsDbContext"/> and applied, in registration order, after the CMS's own entity
/// configurations. This is the seam a package consumer uses to add its own EF-backed content
/// types without editing CMS source.
/// </summary>
public interface ICmsModelExtension
{
    void Configure(ModelBuilder modelBuilder);
}

/// <summary>
/// Applies every <c>IEntityTypeConfiguration&lt;T&gt;</c> found in a specific assembly.
/// </summary>
public sealed class AssemblyModelExtension : ICmsModelExtension
{
    private readonly System.Reflection.Assembly _assembly;

    public AssemblyModelExtension(System.Reflection.Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    public void Configure(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(_assembly);
}

/// <summary>
/// Applies an arbitrary <see cref="Action{T}"/> against the model builder.
/// </summary>
public sealed class DelegateModelExtension : ICmsModelExtension
{
    private readonly Action<ModelBuilder> _configure;

    public DelegateModelExtension(Action<ModelBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    public void Configure(ModelBuilder modelBuilder) => _configure(modelBuilder);
}
