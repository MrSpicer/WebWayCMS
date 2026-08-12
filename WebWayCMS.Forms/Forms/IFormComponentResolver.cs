namespace WebWayCMS.Forms;

/// <summary>
/// Pure logic over <see cref="IFormComponentRegistry"/>, unit-testable without a database.
/// Resolves the best form component for a given <see cref="FormPropertyInfo"/>.
/// </summary>
public interface IFormComponentResolver
{
    FormComponentInfo? Resolve(FormPropertyInfo prop);
}
