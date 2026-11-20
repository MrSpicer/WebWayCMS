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
            Versions = versions.Select(v => VersionItemViewModel.From(v, maxVersion, v.Version.Title ?? string.Empty)).ToList()
        };
    }
}
