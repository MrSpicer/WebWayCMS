using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.FormComponentRegistration;

public sealed class FormComponentRegistrationIndexViewModel
{
    public List<FormComponentRegistrationDTO> Registrations { get; set; } = new();
}
