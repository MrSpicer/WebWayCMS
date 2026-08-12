using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Resolution order:
/// 1. <c>prop.FormComponent</c> is non-empty → <c>GetByName</c>
/// 2. <c>GetForEditorType(prop.EditorType)</c>
/// 3. <c>GetDefaultFor(prop.PropertyType)</c> (unwraps Nullable&lt;&gt;; enum → the Dropdown component)
/// 4. fall back to <c>"Text"</c>; if that is missing too, return <c>null</c>.
/// </summary>
public sealed class FormComponentResolver : IFormComponentResolver
{
    private readonly IFormComponentRegistry _registry;

    public FormComponentResolver(IFormComponentRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public FormComponentInfo? Resolve(FormPropertyInfo prop)
    {
        if (prop == null)
            return null;

        // 1. Explicit FormComponent name
        if (!string.IsNullOrEmpty(prop.FormComponent))
        {
            var byName = _registry.GetByName(prop.FormComponent);
            if (byName != null)
                return byName;
        }

        // 2. EditorType alias
        var byEditorType = _registry.GetForEditorType(prop.EditorType);
        if (byEditorType != null)
            return byEditorType;

        // 3. Default for CLR type
        var clrType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        var byDefault = _registry.GetDefaultFor(clrType);
        if (byDefault != null)
            return byDefault;

        // 4. Fallback to "Text"
        var textComponent = _registry.GetByName("Text");
        if (textComponent != null)
            return textComponent;

        return null;
    }
}
