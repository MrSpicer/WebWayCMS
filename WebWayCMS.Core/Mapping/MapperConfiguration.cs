namespace WebWayCMS.Mapping;

/// <summary>Configures the set of mappings used to build an <see cref="IMapper"/>.</summary>
public interface IMapperConfigurationExpression
{
    /// <summary>Adds all mappings declared by <paramref name="profile"/>.</summary>
    void AddProfile(Profile profile);
}

/// <summary>
/// Collects mappings from one or more <see cref="Profile"/> instances and produces an
/// <see cref="IMapper"/> via <see cref="CreateMapper"/>.
/// </summary>
public sealed class MapperConfiguration : IMapperConfigurationExpression
{
    private readonly Dictionary<(Type Source, Type Destination), Func<object, object>> _maps = new();

    public MapperConfiguration(Action<IMapperConfigurationExpression> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this);
    }

    public void AddProfile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        foreach (var (key, converter) in profile.Maps)
            _maps[key] = converter;
    }

    /// <summary>Builds an <see cref="IMapper"/> over the configured mappings.</summary>
    public IMapper CreateMapper() => new Mapper(_maps);
}
