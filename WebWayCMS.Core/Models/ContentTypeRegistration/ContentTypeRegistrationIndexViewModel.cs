using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.ContentTypeRegistration;

public sealed class ContentTypeRegistrationIndexViewModel
{
    public List<ContentTypeRegistrationDTO> Registrations { get; set; } = [];

    /// <summary>Keys of registrations whose content type is no longer registered in code (orphans).</summary>
    public IReadOnlySet<string> OrphanedKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
