using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// Minimal implementations that keep the interface default methods, so those default bodies
/// (defined in WebWayCMS.Core) are executed and counted.
/// </summary>
internal class MinimalHandler : IAdminCrudHandler
{
    public string ContentType => "minimal";
    public string DisplayName => "Minimal";
    public string[]? WriteRoles => null;
    public string IndexViewPath => "i";
    public string UpsertViewPath => "u";
    public Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");
    public Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default) => Task.FromResult<object?>(null);
    public object CreateEmptyUpsertViewModel() => new object();
    public Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default) => Task.FromResult(new AdminSaveResult(true));
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
    public bool HasSecondaryApiList => false;
    public Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
    public IAdminRegistryHandler? RegistryHandler => null;
}
