using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Controllers.Admin.Handlers;
using WebWayCMS.Forms;
using WebWayCMS.Models.ContentZone;
using WebWayCMS.Pages;

namespace WebWayCMS.Services.Relations;

/// <summary>A <c>[ContentRelation]</c> declared by a content type's upsert view model or a configuration type.</summary>
public sealed record ContentRelationDeclaration(
    string SourceContentType,
    Type ModelType,
    RelationPropertyInfo Property,
    string? RelationName = null)
{
    public string Name => RelationName ?? Property.Name;

    public ContentRelationAttribute Attribute => Property.Attribute;
}

/// <summary>
/// Every relation declared by the registered admin content types, widget configuration types and page
/// configuration types, read from each model (admin mode only). Built and validated on first use; a
/// declaration that breaks a rule throws, the same fail-fast precedent as two handlers sharing a content
/// type key. Admin startup resolves it once so a bad declaration stops the boot.
/// </summary>
public sealed class ContentRelationRegistry
{
    /// <summary>The zone item store's content type key: widget configuration relations are owned by it.</summary>
    public const string ContentZoneItemContentType = "contentzoneitems";

    /// <summary>Config relation names are prefixed so they can't collide with a type's own upsert relations.</summary>
    public const string ConfigPrefix = RelationProperties.ConfigPrefix;

    private readonly IAdminHandlerRegistry _handlers;
    private readonly IWidgetRegistry? _widgets;
    private readonly IPageControllerRegistry? _pages;
    private Dictionary<string, IReadOnlyList<ContentRelationDeclaration>>? _bySource;

    public ContentRelationRegistry(
        IAdminHandlerRegistry handlers,
        IWidgetRegistry? widgets = null,
        IPageControllerRegistry? pages = null)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _widgets = widgets;
        _pages = pages;
    }

    /// <summary>The relations <paramref name="sourceContentType"/> declares (none for an unknown type).</summary>
    public IReadOnlyList<ContentRelationDeclaration> Get(string sourceContentType)
        => Declarations().GetValueOrDefault(sourceContentType) ?? [];

    /// <summary>The declaration of relation <paramref name="name"/> on <paramref name="sourceContentType"/>, if any.</summary>
    public ContentRelationDeclaration? Find(string sourceContentType, string name)
        => Get(sourceContentType).FirstOrDefault(d => d.Name == name);

    /// <summary>The delete policy of a reference; <see cref="RelationDeletePolicy.Restrict"/> when undeclared.</summary>
    public RelationDeletePolicy GetPolicy(string sourceContentType, string name)
        => Find(sourceContentType, name)?.Attribute.OnTargetDelete ?? RelationDeletePolicy.Restrict;

    /// <summary>
    /// Every containment declared by an admin content type whose target is <paramref name="contentType"/>
    /// (the key or its singular, as the entity picker resolves it): the kinds of children it can hold.
    /// </summary>
    public IReadOnlyList<ContentRelationDeclaration> GetContainmentsTargeting(string contentType)
        => Declarations().Values
            .SelectMany(d => d)
            .Where(d => d.Attribute.Kind == ContentRelationKind.Containment
                     && RelationBinder.IsTargetType(contentType, d.Attribute.TargetContentType))
            .ToList();

    /// <summary>Builds the registry now, throwing on the first invalid declaration.</summary>
    public void Validate() => Declarations();

    private Dictionary<string, IReadOnlyList<ContentRelationDeclaration>> Declarations()
        => _bySource ??= Build();

    private Dictionary<string, IReadOnlyList<ContentRelationDeclaration>> Build()
    {
        var bySource = new Dictionary<string, IReadOnlyList<ContentRelationDeclaration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in _handlers.All)
        {
            var modelType = handler.CreateEmptyUpsertViewModel().GetType();
            var declarations = Describe(handler.ContentType, modelType);
            foreach (var declaration in declarations)
                Check(declaration, declarations);
            bySource[handler.ContentType] = declarations;
        }

        // Widget configuration types are owned by the zone item version, page configuration types by the
        // page version; both use the config. prefix so they never collide with upsert relations.
        AddConfigDeclarations(bySource, ContentZoneItemContentType,
            _widgets?.GetAllComponents()
                .Select(w => WidgetConfigurationJson.ResolveType(w.ConfigurationTypeName))
                .OfType<Type>()
            ?? []);

        AddConfigDeclarations(bySource, "pages",
            _pages?.GetAllControllers().Select(c => c.ConfigurationType).OfType<Type>()
            ?? []);

        return bySource;
    }

    private static List<ContentRelationDeclaration> Describe(string sourceContentType, Type modelType)
        => RelationProperties.Describe(modelType)
            .Select(p => new ContentRelationDeclaration(sourceContentType, modelType, p))
            .ToList();

    private void AddConfigDeclarations(
        Dictionary<string, IReadOnlyList<ContentRelationDeclaration>> bySource,
        string sourceContentType,
        IEnumerable<Type> configurationTypes)
    {
        var declarations = bySource.GetValueOrDefault(sourceContentType)?.ToList() ?? [];
        foreach (var type in configurationTypes.Distinct())
        {
            declarations.AddRange(RelationProperties.Describe(type)
                .Select(p => new ContentRelationDeclaration(sourceContentType, type, p, ConfigPrefix + p.Name)));
        }
        if (declarations.Count > 0)
            bySource[sourceContentType] = declarations;

        foreach (var declaration in declarations)
            Check(declaration, declarations);
    }

    private void Check(ContentRelationDeclaration declaration, List<ContentRelationDeclaration> siblings)
    {
        var attr = declaration.Attribute;
        var isContainment = attr.Kind == ContentRelationKind.Containment;

        if (isContainment && declaration.Property.IsMany)
            throw Invalid(declaration, "is a containment, so it must be a single Guid (a child has exactly one parent)");
        if (attr.Ordered && !isContainment)
            throw Invalid(declaration, "sets Ordered, which only a containment may set");
        if (!attr.ShowInParentAdmin && !isContainment)
            throw Invalid(declaration, "turns off ShowInParentAdmin, which only a containment has");
        if (attr.IsOnTargetDeleteSet && isContainment)
            throw Invalid(declaration, "sets OnTargetDelete, which a containment may not set (its children are deleted with it)");
        if (_handlers.GetHandler(attr.TargetContentType) == null && _handlers.GetHandler(attr.TargetContentType + "s") == null)
            throw Invalid(declaration, $"targets '{attr.TargetContentType}', which is not a registered content type");
        if (siblings.Count(d => d.Name == declaration.Name) > 1)
            throw Invalid(declaration, "shares its name with another relation on the same type");
    }

    private static InvalidOperationException Invalid(ContentRelationDeclaration declaration, string rule)
        => new($"Content relation '{declaration.ModelType.Name}.{declaration.Name}' on content type '{declaration.SourceContentType}' {rule}.");
}
