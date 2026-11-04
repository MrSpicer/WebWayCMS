using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

namespace WebWayCMS.Routing.Tests;

[PageController("Custom Display", typeof(SamplePageConfig), Category = "Content", Order = 2, Description = "d", IconClass = "fa")]
public class ConfiguredController : Controller
{
}
