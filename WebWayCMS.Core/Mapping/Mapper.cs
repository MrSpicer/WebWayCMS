namespace WebWayCMS.Mapping;

/// <summary>Resolves and invokes the registered converter for a given source/destination pair.</summary>
internal sealed class Mapper : IMapper
{
    private readonly IReadOnlyDictionary<(Type Source, Type Destination), Func<object, object>> _maps;

    public Mapper(IReadOnlyDictionary<(Type Source, Type Destination), Func<object, object>> maps)
    {
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
    }

    public TDestination Map<TDestination>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Map<TDestination>(source, source.GetType());
    }

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Map<TDestination>(source, typeof(TSource));
    }

    private TDestination Map<TDestination>(object source, Type sourceType)
    {
        var key = (sourceType, typeof(TDestination));
        if (!_maps.TryGetValue(key, out var converter))
            throw new InvalidOperationException(
                $"No mapping registered from '{sourceType.FullName}' to '{typeof(TDestination).FullName}'.");

        return (TDestination)converter(source);
    }
}
