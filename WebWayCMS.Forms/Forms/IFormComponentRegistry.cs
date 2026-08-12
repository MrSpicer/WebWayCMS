using WebWayCMS.Attributes;

namespace WebWayCMS.Forms;

/// <summary>
/// Read-only registry of available form components, backed by the
/// <c>FormComponentRegistration</c> content type.
/// </summary>
public interface IFormComponentRegistry
{
    FormComponentInfo? GetByName(string name);
    FormComponentInfo? GetForEditorType(EditorType editorType);
    FormComponentInfo? GetDefaultFor(Type clrType);
    IReadOnlyList<FormComponentInfo> GetAll();
    void Invalidate();
}
