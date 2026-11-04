using Microsoft.EntityFrameworkCore;

namespace WebWayCMS.Data.DbContexts;

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
