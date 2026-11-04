using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

namespace WebWayCMS.Routing.Tests;

[PageController("No Ctor", typeof(NoDefaultCtorConfig), Category = "Content")]
public class BrokenConfigController : Controller
{
}
