using System.Collections.Frozen;

using Microsoft.Extensions.DependencyInjection;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// The registered admin content-type keys, read once (a singleton). <see cref="AdminContentTypeConstraint"/>
/// runs on every incoming <c>/wadmin/*</c> request, CMS pages such as <c>/wadmin/users</c> included; resolving
/// the scoped <see cref="IAdminHandlerRegistry"/> there would build every handler and its dependencies per
/// request just to answer "is this a content type key". The handler set is fixed when the container is built,
/// so the keys are taken from the first request's registry and kept.
/// </summary>
public sealed class AdminContentTypeKeys
{
    private FrozenSet<string>? _keys;

    /// <summary>
    /// Whether <paramref name="contentType"/> is a registered handler key (case-insensitive). The first call loads
    /// the keys from <paramref name="requestServices"/>' registry; without one it answers false and loads nothing.
    /// </summary>
    public bool Contains(string contentType, IServiceProvider requestServices)
    {
        ArgumentNullException.ThrowIfNull(requestServices);
        var keys = _keys;
        if (keys == null)
        {
            var registry = requestServices.GetService<IAdminHandlerRegistry>();
            if (registry == null)
                return false;
            // A race only loads the same set twice.
            keys = _keys = registry.All.Select(h => h.ContentType).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }

        return keys.Contains(contentType);
    }
}
