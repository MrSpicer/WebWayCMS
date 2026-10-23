using WebWayCMS.Attributes;

namespace WebWayCMS.Services;

/// <summary>
/// Pure, unit-testable fallback logic for resolving a form field render to a view file. Given the
/// form component name, the active render mode, and the field's variant
/// (<c>[FormProperty(FieldViewName = ...)]</c>), computes which view file to render. Each step tries
/// <c>{mode}.{variant}</c> then <c>{mode}.default</c>:
///
/// <list type="bullet">
/// <item><b>Write:</b> <c>write</c> → <c>adminwrite</c>.</item>
/// <item><b>Read:</b> <c>read</c> → <c>write</c> → <c>adminread</c> → <c>adminwrite</c>.</item>
/// <item><b>AdminWrite:</b> <c>adminwrite</c> → <c>write</c>.</item>
/// <item><b>AdminRead:</b> <c>adminread</c> → <c>adminwrite</c> → <c>read</c> → <c>write</c>.</item>
/// </list>
///
/// A read render prefers its own context's write view over the other context's read view, and every
/// mode falls back to the other context, so the shipped admin views (<c>adminwrite.default</c> /
/// <c>adminread.default</c>) render on a public page until a site supplies its own, and a host
/// component that ships only public views still renders in the admin. An empty or "default" variant
/// always means the mode's default view.
/// </summary>
public interface IFormFieldViewResolver
{
    /// <summary>
    /// Runs the per-mode fallback chain (see the type documentation). Returns the view file name to
    /// render (e.g. <c>adminwrite.default</c>), or null when the component has no view on the chain.
    /// </summary>
    string? Resolve(string componentName, FormFieldRenderMode mode, string? variant);
}

/// <summary>
/// Default <see cref="IFormFieldViewResolver"/> implementation over <see cref="IViewDiscoveryService"/>.
/// </summary>
public sealed class FormFieldViewResolver : IFormFieldViewResolver
{
    private static readonly IReadOnlyDictionary<FormFieldRenderMode, FormFieldRenderMode[]> Chains =
        new Dictionary<FormFieldRenderMode, FormFieldRenderMode[]>
        {
            [FormFieldRenderMode.Write] = [FormFieldRenderMode.Write, FormFieldRenderMode.AdminWrite],
            [FormFieldRenderMode.Read] =
                [FormFieldRenderMode.Read, FormFieldRenderMode.Write, FormFieldRenderMode.AdminRead, FormFieldRenderMode.AdminWrite],
            [FormFieldRenderMode.AdminWrite] = [FormFieldRenderMode.AdminWrite, FormFieldRenderMode.Write],
            [FormFieldRenderMode.AdminRead] =
                [FormFieldRenderMode.AdminRead, FormFieldRenderMode.AdminWrite, FormFieldRenderMode.Read, FormFieldRenderMode.Write]
        };

    private readonly IViewDiscoveryService _viewDiscoveryService;

    public FormFieldViewResolver(IViewDiscoveryService viewDiscoveryService)
    {
        _viewDiscoveryService = viewDiscoveryService ?? throw new ArgumentNullException(nameof(viewDiscoveryService));
    }

    public string? Resolve(string componentName, FormFieldRenderMode mode, string? variant)
    {
        foreach (var step in Chains[mode])
        {
            if (_viewDiscoveryService.TryResolveView(componentName, step, variant, out var view)
                || _viewDiscoveryService.TryResolveView(componentName, step, "default", out view))
                return view;
        }

        return null;
    }
}
