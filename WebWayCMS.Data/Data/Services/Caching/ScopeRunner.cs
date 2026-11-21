using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// Runs a probe against a fresh context in a fresh scope — never the ambient saving context, so the
/// probe (a generation bump) can't re-enter the change-detection interceptor watching that save.
/// </summary>
internal sealed class ScopeRunner
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopeRunner(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public async Task<T> RunAsync<T>(Func<CmsDbContext, CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        return await action(context, ct);
    }
}
