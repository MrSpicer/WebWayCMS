using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Models.ContentZone;

public interface IContentZoneObject
{
    /// <summary>
    /// The unique identifier of this content zone item.
    /// </summary>
    Guid Id { get; set; }

    int Ordinal { get; set; }
    Guid ZoneId { get; set; }

    string ComponentName { get; set; }
    Object ComponentProperties { get; set; }
}