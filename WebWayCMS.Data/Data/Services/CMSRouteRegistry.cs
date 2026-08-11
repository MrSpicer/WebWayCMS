using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public class CMSRouteRegistry : ICMSRouteRegistry
{
    private const int TtlSeconds = 60;

    private readonly IServiceScopeFactory _scopeFactory;

    private volatile List<CMSRouteDTO> _routes = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _loaded;

    public CMSRouteRegistry(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public IReadOnlyList<CMSRouteDTO> GetActiveRoutes()
    {
        EnsureLoaded();
        return _routes;
    }

    public void Invalidate()
    {
        _lastRefresh = DateTime.MinValue;
    }

    private void EnsureLoaded()
    {
        if (_loaded && (DateTime.UtcNow - _lastRefresh).TotalSeconds < TtlSeconds)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var routeService = scope.ServiceProvider.GetRequiredService<ICMSRouteService>();
            var dtos = routeService.GetActiveRoutesAsync().GetAwaiter().GetResult();
            _routes = dtos;
            _loaded = true;
            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load routes from database: {ex.Message}");
        }
    }
}
