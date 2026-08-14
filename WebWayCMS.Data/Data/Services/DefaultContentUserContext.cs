namespace WebWayCMS.Data.Services;

/// <summary>
/// Default <see cref="IContentUserContext"/> used when no identity host supplies one — always null.
/// </summary>
public sealed class DefaultContentUserContext : IContentUserContext
{
    public Guid? CurrentUserId => null;
}
