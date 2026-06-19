using WebWayCMS.Data.Models;
using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class ContentZoneResolver : IContentZoneResolver
{
    private readonly IContentZoneModel _model;

    public ContentZoneResolver(IContentZoneModel model)
        => _model = model ?? throw new ArgumentNullException(nameof(model));

    public async Task<ContentZoneViewModel> ResolveAsync(
        string? zoneName,
        bool isGlobal,
        Guid? zoneId,
        PageDTO? page,
        Guid? parentZoneId,
        CancellationToken ct = default)
    {
        ContentZoneViewModel? vm;
        Guid? pageMasterId = null;

        if (zoneId.HasValue)
        {
            // Direct lookup by zone id - bypasses name/page resolution.
            vm = await _model.GetViewModelByIdAsync(zoneId.Value, ct);
        }
        else if (string.IsNullOrWhiteSpace(zoneName))
        {
            return Empty(zoneName, pageMasterId);
        }
        else if (!isGlobal && page is not null)
        {
            pageMasterId = page.ContentMeta.MasterId;

            // Page-scoped zone: nested inside another zone, or a top-level page slot.
            vm = parentZoneId.HasValue
                ? await _model.GetOrCreateViewModelByZoneSlotAsync(parentZoneId.Value, zoneName, ct)
                : await _model.GetOrCreateViewModelByPageSlotAsync(pageMasterId.Value, zoneName, ct);
        }
        else
        {
            // Global or layout zone: get or create by name.
            vm = await _model.GetOrCreateViewModelAsync(zoneName, ct);
        }

        if (vm is null)
            return Empty(zoneName, pageMasterId);

        vm.RawZoneName = zoneName ?? vm.Name;
        vm.ParentPageMasterId = pageMasterId;
        return vm;
    }

    private static ContentZoneViewModel Empty(string? zoneName, Guid? pageMasterId) => new()
    {
        Id = Guid.Empty,
        Name = zoneName ?? string.Empty,
        RawZoneName = zoneName ?? string.Empty,
        ZoneObjects = new List<ContentZoneObject>(),
        ParentPageMasterId = pageMasterId,
    };
}
