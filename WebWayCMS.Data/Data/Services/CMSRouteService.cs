using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

public sealed class CMSRouteService : ICMSRouteService
{
    private readonly CmsDbContext _context;

    public CMSRouteService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CMSRouteMatchResult?> MatchRouteAsync(string path, CancellationToken ct = default)
    {
        path = NormalizePattern(path);

        var activeRoutes = await GetActiveRoutesAsync(ct);

        foreach (var route in activeRoutes.OrderBy(r => r.Order))
        {
            if (route.IsReserved)
                continue;

            var match = TryMatchPattern(route.Pattern, path);
            if (match != null)
            {
                return new CMSRouteMatchResult
                {
                    Route = route,
                    RouteValues = match
                };
            }
        }

        return null;
    }

    public async Task<List<CMSRouteDTO>> GetActiveRoutesAsync(CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .Where(r => r.ContentMeta.IsPublished
                && !r.ContentMeta.IsDeleted
                && !_context.Set<CMSRouteDTO>().Any(r2 =>
                    r2.ContentMeta.MasterId == r.ContentMeta.MasterId
                    && r2.ContentMeta.Version > r.ContentMeta.Version))
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Pattern.Length)
            .ToListAsync(ct);
    }

    public async Task<List<CMSRouteDTO>> GetByOwningContentAsync(Guid owningContentMasterId, CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .Where(r => r.OwningContentMasterId == owningContentMasterId
                && !r.ContentMeta.IsDeleted
                && !_context.Set<CMSRouteDTO>().Any(r2 =>
                    r2.ContentMeta.MasterId == r.ContentMeta.MasterId
                    && r2.ContentMeta.Version > r.ContentMeta.Version))
            .ToListAsync(ct);
    }

    public async Task<CMSRouteDTO?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<CMSRouteDTO>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ContentId == id, ct);
    }

    public async Task<bool> IsPatternAvailableAsync(string pattern, Guid? excludeMasterId = null, CancellationToken ct = default)
    {
        pattern = NormalizePattern(pattern);

        var query = _context.Set<CMSRouteDTO>()
            .Where(r => r.Pattern == pattern
                && !r.ContentMeta.IsDeleted
                && !_context.Set<CMSRouteDTO>().Any(r2 =>
                    r2.ContentMeta.MasterId == r.ContentMeta.MasterId
                    && r2.ContentMeta.Version > r.ContentMeta.Version));

        if (excludeMasterId.HasValue)
            query = query.Where(r => r.OwningContentMasterId != excludeMasterId.Value);

        return !await query.AnyAsync(ct);
    }

    public async Task<CMSRouteDTO> UpsertAsync(CMSRouteDTO route, CancellationToken ct = default)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));

        route.Pattern = NormalizePattern(route.Pattern);

        var meta = route.ContentMeta;

        var existing = await _context.Set<CMSRouteDTO>()
            .Where(r => r.OwningContentMasterId == route.OwningContentMasterId
                && !r.ContentMeta.IsDeleted
                && !_context.Set<CMSRouteDTO>().Any(r2 =>
                    r2.ContentMeta.MasterId == r.ContentMeta.MasterId
                    && r2.ContentMeta.Version > r.ContentMeta.Version))
            .FirstOrDefaultAsync(ct)
            ?? await _context.Set<CMSRouteDTO>()
            .Where(r => r.Pattern == route.Pattern
                && !r.ContentMeta.IsDeleted
                && !_context.Set<CMSRouteDTO>().Any(r2 =>
                    r2.ContentMeta.MasterId == r.ContentMeta.MasterId
                    && r2.ContentMeta.Version > r.ContentMeta.Version))
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            _context.Set<CMSRouteDTO>().Remove(existing);
            _context.Remove(existing.ContentMeta);
            await _context.SaveChangesAsync(ct);
        }

        if (meta.Id == Guid.Empty)
            meta.Id = Guid.NewGuid();

        route.ContentId = meta.Id;
        meta.MasterId = meta.Id;
        meta.Version = 0;

        var now = DateTime.UtcNow;
        if (meta.CreationDate == default)
            meta.CreationDate = now;
        meta.ModificationDate = now;
        if (meta.IsPublished && meta.PublicationDate == default)
            meta.PublicationDate = now;

        _context.Set<CMSRouteDTO>().Add(route);
        await _context.SaveChangesAsync(ct);
        return route;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.Set<CMSRouteDTO>()
            .FirstOrDefaultAsync(r => r.ContentId == id, ct);
        if (entity == null) return false;

        var allVersions = await _context.Set<CMSRouteDTO>()
            .Where(r => r.ContentMeta.MasterId == entity.ContentMeta.MasterId)
            .ToListAsync(ct);

        _context.Set<CMSRouteDTO>().RemoveRange(allVersions);
        _context.RemoveRange(allVersions.Select(v => v.ContentMeta));
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeactivateByOwningContentAsync(Guid owningContentMasterId, CancellationToken ct = default)
    {
        var activeRoutes = await _context.Set<CMSRouteDTO>()
            .Where(r => r.OwningContentMasterId == owningContentMasterId
                && r.ContentMeta.IsPublished
                && !r.ContentMeta.IsDeleted)
            .ToListAsync(ct);

        if (activeRoutes.Count == 0) return false;

        foreach (var route in activeRoutes)
        {
            route.ContentMeta.IsPublished = false;
        }

        _context.Set<CMSRouteDTO>().UpdateRange(activeRoutes);
        await _context.SaveChangesAsync(ct);
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
            var pathSeg = i < pathSegments.Length ? pathSegments[i] : string.Empty;

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
        if (!patternSeg.Contains('{'))
            return string.Equals(patternSeg, pathSeg, StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>()
                : null;

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

        var literalPrefixes = patternParts.Where(p => !p.isParam).Select(p => p.value).ToList();
        var fullLiteral = string.Concat(literalPrefixes);
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

        if (string.Equals(patternSeg, pathSeg, StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, string>();

        return null;
    }
}
