using Microsoft.EntityFrameworkCore;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services.ContentTypes;

/// <summary>
/// Scoped: the published registrations are read once per request (the admin navbar, the children panels and the
/// entity pickers of one page all ask) and reused. Should two registrations share a key, the oldest wins.
/// </summary>
public sealed class ContentTypeRegistrationService : IContentTypeRegistrationService
{
    private readonly CmsDbContext _context;
    private IReadOnlyDictionary<string, ContentTypeRegistrationDTO>? _published;

    public ContentTypeRegistrationService(CmsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyDictionary<string, ContentTypeRegistrationDTO>> GetPublishedByKeyAsync(CancellationToken ct = default)
        => _published ??= ByKey(await PublishedQuery().ToListAsync(ct));

    public IReadOnlyDictionary<string, ContentTypeRegistrationDTO> GetPublishedByKey()
        => _published ??= ByKey(PublishedQuery().ToList());

    private IQueryable<ContentTypeRegistrationDTO> PublishedQuery()
        => _context.Set<ContentTypeRegistrationDTO>()
            .AsNoTracking()
            .Published()
            .OrderBy(r => r.Version.Node.CreatedUtc);

    private static Dictionary<string, ContentTypeRegistrationDTO> ByKey(List<ContentTypeRegistrationDTO> rows)
    {
        var byKey = new Dictionary<string, ContentTypeRegistrationDTO>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            byKey.TryAdd(row.ContentTypeKey, row);
        return byKey;
    }
}
