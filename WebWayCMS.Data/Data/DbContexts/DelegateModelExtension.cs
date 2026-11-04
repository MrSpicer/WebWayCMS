using Microsoft.EntityFrameworkCore;

namespace WebWayCMS.Data.DbContexts;

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
