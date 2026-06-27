namespace WebWayCMS.Mapping;

/// <summary>
/// Base class for grouping type-to-type mappings registered in code. Derive from this and call
/// <see cref="CreateMap{TSource, TDestination}"/> in the constructor for each mapping.
/// </summary>
public abstract class Profile
{
    private readonly Dictionary<(Type Source, Type Destination), Func<object, object>> _maps = new();

    internal IReadOnlyDictionary<(Type Source, Type Destination), Func<object, object>> Maps => _maps;

    /// <summary>
    /// Registers a mapping from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>.
    /// The converter is the complete mapping logic for the pair.
    /// </summary>
    protected void CreateMap<TSource, TDestination>(Func<TSource, TDestination> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _maps[(typeof(TSource), typeof(TDestination))] = source => converter((TSource)source)!;
    }
}