using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Shared;

public abstract class VersionedModel<TDto> where TDto : class, IContent
{
    protected abstract Task<List<TDto>> GetAllVersionsAsync(Guid masterId, CancellationToken ct);
    protected abstract Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct);
    protected abstract string VersionHistoryContentType { get; }
    protected abstract string GetVersionHistoryBackUrl(string? parentKey = null);

    protected async Task<VersionHistoryViewModel?> BuildVersionHistoryAsync(
        Guid masterId,
        string? parentKey = null,
        string? childType = null,
        CancellationToken ct = default)
    {
        var versions = await GetAllVersionsAsync(masterId, ct);
        if (!versions.Any()) return null;
        var maxVersion = versions.Max(v => v.ContentMeta.Version);
        return new VersionHistoryViewModel
        {
            ContentType = VersionHistoryContentType,
            MasterId = masterId,
            ItemTitle = versions.First().ContentMeta.Title ?? string.Empty,
            BackUrl = GetVersionHistoryBackUrl(parentKey),
            ParentKey = parentKey,
            ChildType = childType,
            Versions = versions.Select(v => new VersionItemViewModel
            {
                Id = v.ContentMeta.Id,
                Version = v.ContentMeta.Version,
                Title = v.ContentMeta.Title ?? string.Empty,
                CreationDate = v.ContentMeta.CreationDate,
                ModificationDate = v.ContentMeta.ModificationDate,
                IsPublished = v.ContentMeta.IsPublished,
                IsDeleted = v.ContentMeta.IsDeleted,
                IsLatest = v.ContentMeta.Version == maxVersion
            }).ToList()
        };
    }
}
