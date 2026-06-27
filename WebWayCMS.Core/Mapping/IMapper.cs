namespace WebWayCMS.Mapping;

/// <summary>
/// Maps a source object to a destination type using mappings registered via <see cref="Profile"/>.
/// </summary>
public interface IMapper
{
    /// <summary>Maps <paramref name="source"/> to <typeparamref name="TDestination"/>.</summary>
    TDestination Map<TDestination>(object source);

    /// <summary>Maps <paramref name="source"/> to <typeparamref name="TDestination"/> using the declared source type.</summary>
    TDestination Map<TSource, TDestination>(TSource source);
}