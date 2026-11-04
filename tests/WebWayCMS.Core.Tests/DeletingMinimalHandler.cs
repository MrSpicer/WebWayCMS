using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

/// <summary>A MinimalHandler whose version delete succeeds, for the TryDeleteVersionAsync default's success path.</summary>
internal sealed class DeletingMinimalHandler : MinimalHandler, IAdminCrudHandler
{
    public Task<bool> DeleteVersionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
}
