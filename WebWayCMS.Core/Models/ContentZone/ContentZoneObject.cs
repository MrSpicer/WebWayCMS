using System.Threading;
using System.Threading.Tasks;

namespace WebWayCMS.Models.ContentZone;

//probably move this to Data and use as dto
public class ContentZoneObject : IContentZoneObject
{
	/// <summary>
	/// The unique identifier of this content zone item.
	/// </summary>
	public Guid Id { get; set; } = Guid.Empty;

	public int Ordinal { get; set; } = 0;
	public Guid ZoneId { get; set; }

	public string ComponentName { get; set; } = string.Empty;

	/// <summary>Optional host-provided view name; empty renders the component's default widget.</summary>
	public string ViewName { get; set; } = string.Empty;

	public object ComponentProperties { get; set; } = default!;
}
