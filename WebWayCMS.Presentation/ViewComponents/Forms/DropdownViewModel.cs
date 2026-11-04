using WebWayCMS.Forms;

namespace WebWayCMS.ViewComponents.Forms;

public sealed class DropdownViewModel
{
    public FormFieldContext Field { get; set; } = null!;
    public Dictionary<string, string> Options { get; set; } = new();
}
