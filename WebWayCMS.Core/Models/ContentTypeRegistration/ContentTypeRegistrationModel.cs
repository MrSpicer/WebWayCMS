using Microsoft.AspNetCore.Http;

using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Data.Services.ContentTypes;
using WebWayCMS.Models.Shared;
using WebWayCMS.Services.Relations;

namespace WebWayCMS.Models.ContentTypeRegistration;

/// <summary>
/// Admin CRUD for the content type registry (<c>/wadmin/contenttypes</c>, Admin only). Registrations come from
/// code (the boot sync inserts one per registered content type), so they can be edited, published and restored,
/// never created or deleted here or over MCP; an edit changes only the admin-owned fields.
/// </summary>
public sealed class ContentTypeRegistrationModel : AdminCrudModel<ContentTypeRegistrationDTO>
{
    public const string ContentTypeKey = "contenttypes";
    public const string CreateRefusedMessage = "Content type registrations are created from code (a registered content store), not here.";
    public const string DeleteRefusedMessage = "Content type registrations are managed by code and can't be deleted.";

    private readonly IContentStore<ContentTypeRegistrationDTO> _store;
    private readonly IContentTypeRegistry _contentTypes;

    protected override IContentStore<ContentTypeRegistrationDTO> Store => _store;

    public override string ContentType => ContentTypeKey;
    public override string DisplayName => "Content Type";
    public override string IndexViewPath => "~/Views/ContentTypeRegistration/Index.cshtml";
    public override string UpsertViewPath => "~/Views/ContentTypeRegistration/ContentTypeRegistrationUpsert.cshtml";

    public ContentTypeRegistrationModel(
        IContentStore<ContentTypeRegistrationDTO> store,
        IContentTypeRegistry contentTypes,
        IChangeSetScope changeSetScope,
        IRelationBinder relationBinder,
        IContentDeleteService deletes)
        : base(changeSetScope, relationBinder, deletes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contentTypes = contentTypes ?? throw new ArgumentNullException(nameof(contentTypes));
    }

    public override async Task<object> GetIndexViewModelAsync(CancellationToken ct = default)
    {
        var drafts = await _store.GetAllCurrentDraftsAsync(ct);
        return new ContentTypeRegistrationIndexViewModel
        {
            Registrations = drafts
                .OrderBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Order)
                .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            OrphanedKeys = drafts
                .Where(r => _contentTypes.Get(r.ContentTypeKey) == null)
                .Select(r => r.ContentTypeKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
    }

    public override async Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default)
        => await LoadUpsertAsync(id, ToUpsertViewModel, ct);

    private static ContentTypeRegistrationUpsertViewModel ToUpsertViewModel(ContentTypeRegistrationDTO dto) => new()
    {
        NodeId = dto.Version.Node.Id,
        ExpectedVersionNumber = dto.Version.VersionNumber,
        Title = dto.Version.Title,
        Slug = dto.Version.Slug,
        ContentTypeKey = dto.ContentTypeKey,
        ClrTypeName = dto.ClrTypeName,
        DisplayName = dto.DisplayName,
        PluralDisplayName = dto.PluralDisplayName,
        Description = dto.Description,
        Category = dto.Category,
        IconClass = dto.IconClass,
        Order = dto.Order,
        IsPublished = dto.Version.State == ContentVersionState.Published,
    };

    public override object CreateEmptyUpsertViewModel() => new ContentTypeRegistrationUpsertViewModel();

    protected override async Task<AdminSaveResult> SaveUpsertCoreAsync(object model, CancellationToken ct = default)
    {
        var vm = (ContentTypeRegistrationUpsertViewModel)model;
        if (vm.NodeId is not { } nodeId || nodeId == Guid.Empty)
            return new AdminSaveResult(false, CreateRefusedMessage);

        var existing = await _store.GetCurrentDraftAsync(nodeId, ct);
        if (existing == null)
            return new AdminSaveResult(false, "Content type registration not found.");

        // The key and CLR type stay as the code synced them, whatever the form or an MCP caller sent.
        var dto = existing with
        {
            Version = existing.Version with
            {
                Title = vm.Title,
                Slug = vm.Slug ?? string.Empty,
            },
            DisplayName = vm.DisplayName,
            PluralDisplayName = vm.PluralDisplayName,
            Description = vm.Description ?? string.Empty,
            Category = vm.Category,
            IconClass = vm.IconClass ?? string.Empty,
            Order = vm.Order,
        };

        return await SaveDraftAsync(dto, vm, "Save failed.", ct);
    }

    public override Task<(bool Success, string? ErrorMessage, int DeletedDescendants)> TryDeleteAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<(bool, string?, int)>((false, DeleteRefusedMessage, 0));

    protected override Task<bool> DeleteSelfAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(false);

    protected override string PickerTitle(ContentTypeRegistrationDTO item) => item.DisplayName;

    public override Task<object?> GetRestoreVersionViewModelAsync(Guid historicalId, CancellationToken ct = default)
        => MapRestoreAsync(historicalId, ToUpsertViewModel, ct);
}
