using WebWayCMS.Attributes;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Presentation.Components.Widgets;

namespace WebWayCMS.Presentation.Rendering;

/// <inheritdoc />
public sealed class FormOptionsProvider : IFormOptionsProvider
{
    private readonly IAdminHandlerRegistry _handlers;

    public FormOptionsProvider(IAdminHandlerRegistry handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public async Task<IReadOnlyList<FormOption>> GetOptionsAsync(FormPropertyInfo prop, CancellationToken ct = default)
    {
        // ViewPicker is used only by the Layout widget to pick among its built-in layout templates;
        // host component views are selected per zone-item via IContentZoneViewRegistry instead.
        if (prop.EditorType == EditorType.ViewPicker && string.Equals(prop.ViewComponentName, "Layout", StringComparison.OrdinalIgnoreCase))
            return LayoutWidget.LayoutViewNames.Select(v => new FormOption(v, v)).ToList();

        // Guid entity picker: the entities of the field's EntityType, via the admin handler's api list.
        if (prop.EditorType == EditorType.Guid && !string.IsNullOrEmpty(prop.EntityType))
            return await GetEntityOptionsAsync(prop.EntityType, ct);

        return Array.Empty<FormOption>();
    }

    private async Task<IReadOnlyList<FormOption>> GetEntityOptionsAsync(string entityType, CancellationToken ct)
    {
        var (contentType, secondaryKey) = MapEntity(entityType);
        if (contentType is null)
            return Array.Empty<FormOption>();

        var handler = _handlers.GetHandler(contentType);
        if (handler is null)
            return Array.Empty<FormOption>();

        var items = secondaryKey is null
            ? await handler.GetApiListAsync(ct)
            : await handler.GetSecondaryApiListAsync(secondaryKey, ct);

        return items.Select(ToOption).OfType<FormOption>().ToList();
    }

    // The entity types used by content-zone configs map to admin handler content types; ArticleList
    // is exposed as the "articles" handler's secondary list.
    private static (string? ContentType, string? SecondaryKey) MapEntity(string entityType) => entityType switch
    {
        "ContentBlock" => ("contentblocks", null),
        "ContentZone" => ("contentzones", null),
        "Article" => ("articles", null),
        "ArticleList" => ("articles", "articlelists"),
        _ => (null, null),
    };

    private static FormOption? ToOption(object item)
    {
        var id = ReadString(item, "id");
        if (string.IsNullOrEmpty(id))
            return null;

        var title = ReadString(item, "title");
        return new FormOption(id, string.IsNullOrEmpty(title) ? id : title);
    }

    private static string? ReadString(object item, string name)
    {
        var prop = item.GetType().GetProperty(name);
        return prop is null ? null : Convert.ToString(prop.GetValue(item));
    }
}
