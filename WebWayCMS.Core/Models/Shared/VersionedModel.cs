using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.Shared;

public abstract class VersionedModel<TDto> where TDto : class, IVersionedContent
{
    protected abstract Task<List<TDto>> GetAllVersionsAsync(Guid nodeId, CancellationToken ct);
    protected abstract Task<bool> DeleteVersionCoreAsync(Guid id, CancellationToken ct);
    protected abstract string VersionHistoryContentType { get; }
    protected abstract string GetVersionHistoryBackUrl(string? parentKey = null);

    protected async Task<VersionHistoryViewModel?> BuildVersionHistoryAsync(
        Guid nodeId,
        string? parentKey = null,
        string? childType = null,
        CancellationToken ct = default)
    {
        var versions = await GetAllVersionsAsync(nodeId, ct);
        if (!versions.Any()) return null;
        var maxVersion = versions.Max(v => v.Version.VersionNumber);
        return new VersionHistoryViewModel
        {
            ContentType = VersionHistoryContentType,
            NodeId = nodeId,
            ItemTitle = versions.First().Version.Title ?? string.Empty,
            BackUrl = GetVersionHistoryBackUrl(parentKey),
            ParentKey = parentKey,
            ChildType = childType,
            Versions = versions.Select(v => new VersionItemViewModel
            {
                Id = v.VersionId,
                Version = v.Version.VersionNumber,
                Title = v.Version.Title ?? string.Empty,
                CreationDate = v.Version.Node.CreatedUtc,
                ModificationDate = v.Version.CreatedUtc,
                IsPublished = v.Version.State == ContentVersionState.Published,
                IsDeleted = v.Version.Node.IsDeleted,
                IsLatest = v.Version.VersionNumber == maxVersion,
                CreatedBy = v.Version.CreatedBy,
                ChangeNote = v.Version.ChangeNote,
                State = v.Version.State,
                ChangeSetId = v.Version.ChangeSetId
            }).ToList()
        };
    }
}
