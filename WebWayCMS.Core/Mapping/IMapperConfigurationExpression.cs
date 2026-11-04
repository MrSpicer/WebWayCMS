namespace WebWayCMS.Mapping;

/// <summary>Configures the set of mappings used to build an <see cref="IMapper"/>.</summary>
public interface IMapperConfigurationExpression
{
    /// <summary>Adds all mappings declared by <paramref name="profile"/>.</summary>
    void AddProfile(Profile profile);
}
