using WebWayCMS.Attributes;

namespace WebWayCMS.Services;

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
