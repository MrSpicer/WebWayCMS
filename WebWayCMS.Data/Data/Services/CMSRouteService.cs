using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>
/// Manages CMS routes. Routes are plain rows (no version history) — they are written by Publish and
/// hard-deleted/replaced, never by Save.
/// </summary>
public sealed class CMSRouteService : ICMSRouteService
{
    private readonly CmsDbContext _context;
    private readonly ICMSRouteRegistry _registry;

    public CMSRouteService(CmsDbContext context, ICMSRouteRegistry registry)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Task<CMSRouteMatchResult?> MatchRouteAsync(string path, CancellationToken ct = default)
    {
        path = NormalizePattern(path);

        var activeRoutes = _registry.GetActiveRoutes();

        foreach (var route in activeRoutes.OrderBy(r => r.Order))
        {
            if (route.IsReserved)
                continue;

            var match = TryMatchPattern(route.Pattern, path);
            if (match != null)
            {
                return Task.FromResult<CMSRouteMatchResult?>(new CMSRouteMatchResult
                {
                    Route = route,
                    RouteValues = match
                });
            }
        }

        return Task.FromResult<CMSRouteMatchResult?>(null);
    }

    public async Task<List<CMSRouteDTO>> GetActiveRoutesAsync(CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Pattern.Length)
            .ToListAsync(ct);
    }

    public async Task<List<CMSRouteDTO>> GetAllRoutesAsync(CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Pattern.Length)
            .ToListAsync(ct);
    }

    public async Task<List<CMSRouteDTO>> GetByOwningContentAsync(Guid owningContentNodeId, CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .Where(r => r.OwningContentNodeId == owningContentNodeId)
            .ToListAsync(ct);
    }

    public async Task<CMSRouteDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> IsPatternAvailableAsync(string pattern, Guid? excludeNodeId = null, Guid? excludeRouteId = null, CancellationToken ct = default)
    {
        pattern = NormalizePattern(pattern);

        var query = _context.Set<CMSRouteDTO>()
            .Where(r => r.Pattern == pattern);

        if (excludeNodeId.HasValue)
            query = query.Where(r => r.OwningContentNodeId != excludeNodeId.Value);

        if (excludeRouteId.HasValue)
            query = query.Where(r => r.Id != excludeRouteId.Value);

        return !await query.AnyAsync(ct);
    }

    public async Task<CMSRouteDTO> UpsertAsync(CMSRouteDTO route, CancellationToken ct = default)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));

        route.Pattern = NormalizePattern(route.Pattern);

        var existing = await _context.Set<CMSRouteDTO>()
            .Where(r => (route.OwningContentNodeId.HasValue && r.OwningContentNodeId == route.OwningContentNodeId.Value)
                     || r.Pattern == route.Pattern)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            _context.Set<CMSRouteDTO>().RemoveRange(existing);
            await _context.SaveChangesAsync(ct);
        }

        if (route.Id == Guid.Empty)
            route.Id = Guid.NewGuid();

        _context.Set<CMSRouteDTO>().Add(route);
        await _context.SaveChangesAsync(ct);
        _registry.Invalidate();
        return route;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Set<CMSRouteDTO>()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity == null) return false;

        _context.Set<CMSRouteDTO>().Remove(entity);
        await _context.SaveChangesAsync(ct);
        _registry.Invalidate();
        return true;
    }

    public async Task<bool> DeleteByOwningContentAsync(Guid owningContentNodeId, CancellationToken ct = default)
    {
        var routes = await _context.Set<CMSRouteDTO>()
            .Where(r => r.OwningContentNodeId == owningContentNodeId)
            .ToListAsync(ct);

        if (routes.Count == 0) return false;

        _context.Set<CMSRouteDTO>().RemoveRange(routes);
        await _context.SaveChangesAsync(ct);
        _registry.Invalidate();
        return true;
    }

    internal static string NormalizePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "/";

        pattern = pattern.Trim().ToLowerInvariant();

        if (!pattern.StartsWith('/'))
            pattern = "/" + pattern;

        if (pattern.Length > 1 && pattern.EndsWith('/'))
            pattern = pattern.TrimEnd('/');

        return pattern;
    }

    internal static Dictionary<string, string>? TryMatchPattern(string pattern, string path)
    {
        if (pattern == path)
            return new Dictionary<string, string>();

        if (!pattern.Contains('{'))
            return null;

        var patternSegments = pattern.Trim('/').Split('/');
        var pathSegments = path.Trim('/').Split('/');

        if (patternSegments.Length != pathSegments.Length && !pattern.EndsWith("{**slug}") && !pattern.Contains("{**"))
        {
            var hasOptional = patternSegments.Any(s => s.EndsWith("?}"));
            if (!hasOptional && patternSegments.Length != pathSegments.Length)
                return null;
        }

        var routeValues = new Dictionary<string, string>();
        var minSegments = Math.Min(patternSegments.Length, pathSegments.Length);

        for (int i = 0; i < minSegments; i++)
        {
            var patternSeg = patternSegments[i];
            var pathSeg = pathSegments[i];

            if (patternSeg.StartsWith("{**"))
            {
                var paramName = ExtractParamName(patternSeg, 3);
                var remaining = string.Join("/", pathSegments.Skip(i));
                routeValues[paramName] = remaining;
                return routeValues;
            }

            if (patternSeg.StartsWith('{') && patternSeg.EndsWith('}'))
            {
                var isOptional = patternSeg.EndsWith("?}");
                var (paramName, constraint) = ParseParameter(patternSeg, 1);

                if (constraint != null && !ApplyConstraint(constraint, pathSeg))
                {
                    if (!isOptional)
                        return null;
                    continue;
                }

                if (isOptional && string.IsNullOrEmpty(pathSeg))
                    continue;

                routeValues[paramName] = pathSeg;
            }
            else if (patternSeg.Contains('{') && patternSeg.Contains('}'))
            {
                var result = TryMatchLiteralAndParamSegment(patternSeg, pathSeg);
                if (result == null) return null;
                foreach (var kvp in result)
                    routeValues[kvp.Key] = kvp.Value;
            }
            else
            {
                if (!string.Equals(patternSeg, pathSeg, StringComparison.OrdinalIgnoreCase))
                    return null;
            }
        }

        if (patternSegments.Length > pathSegments.Length)
        {
            for (int i = pathSegments.Length; i < patternSegments.Length; i++)
            {
                var seg = patternSegments[i];
                if (seg.EndsWith("?}") || seg == "{**slug}")
                    continue;
                return null;
            }
        }

        return routeValues;
    }

    private static (string name, string? constraint) ParseParameter(string segment, int start)
    {
        var content = segment.Substring(start, segment.Length - start - (segment.EndsWith("?}") ? 2 : 1));
        var colonIdx = content.IndexOf(':');
        if (colonIdx > 0)
            return (content.Substring(0, colonIdx), content.Substring(colonIdx + 1));
        return (content, null);
    }

    private static string ExtractParamName(string segment, int start)
    {
        return segment.Substring(start, segment.Length - start - 1);
    }

    private static bool ApplyConstraint(string constraint, string value)
    {
        if (constraint.StartsWith("regex(") && constraint.EndsWith(")"))
        {
            var pattern = constraint.Substring(6, constraint.Length - 7);
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(value, pattern);
            }
            catch
            {
                return false;
            }
        }

        if (constraint == "int")
            return int.TryParse(value, out _);

        if (constraint == "guid")
            return Guid.TryParse(value, out _);

        if (constraint == "bool")
            return bool.TryParse(value, out _);

        return true;
    }

    private static Dictionary<string, string>? TryMatchLiteralAndParamSegment(string patternSeg, string pathSeg)
    {
        var patternParts = new List<(bool isParam, string value)>();
        var remaining = patternSeg;
        while (remaining.Length > 0)
        {
            if (remaining.StartsWith('{'))
            {
                var endIdx = remaining.IndexOf('}');
                if (endIdx < 0) return null;
                patternParts.Add((true, remaining.Substring(0, endIdx + 1)));
                remaining = remaining.Substring(endIdx + 1);
            }
            else
            {
                var braceIdx = remaining.IndexOf('{');
                if (braceIdx < 0)
                {
                    patternParts.Add((false, remaining));
                    remaining = string.Empty;
                }
                else
                {
                    patternParts.Add((false, remaining.Substring(0, braceIdx)));
                    remaining = remaining.Substring(braceIdx);
                }
            }
        }

        var routeValues = new Dictionary<string, string>();

        if (patternParts.Count == 2 && !patternParts[0].isParam && patternParts[1].isParam)
        {
            var prefix = patternParts[0].value;
            if (!pathSeg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            var paramValue = pathSeg.Substring(prefix.Length);
            var (paramName, constraint) = ParseParameter(patternParts[1].value, 1);
            if (constraint != null && !ApplyConstraint(constraint, paramValue))
                return null;
            routeValues[paramName] = paramValue;
            return routeValues;
        }

        if (patternParts.Count == 2 && patternParts[0].isParam && !patternParts[1].isParam)
        {
            var suffix = patternParts[1].value;
            if (!pathSeg.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return null;
            var paramValue = pathSeg.Substring(0, pathSeg.Length - suffix.Length);
            var (paramName, constraint) = ParseParameter(patternParts[0].value, 1);
            if (constraint != null && !ApplyConstraint(constraint, paramValue))
                return null;
            routeValues[paramName] = paramValue;
            return routeValues;
        }

        return null;
    }
}
