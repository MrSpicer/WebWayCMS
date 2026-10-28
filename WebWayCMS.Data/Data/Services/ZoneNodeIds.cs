using System.Security.Cryptography;
using System.Text;

namespace WebWayCMS.Data.Services;

/// <summary>
/// The node id of the zone an owner (page or container item) holds in a slot: a name-based (RFC 4122
/// version 5, SHA-1) Guid of (owner, slot). Two concurrent renders that both create the slot's zone
/// therefore collide on the <c>ContentNodes</c> primary key instead of creating two zones. The slot is
/// hashed exactly as given (slot lookups are case-sensitive).
/// </summary>
public static class ZoneNodeIds
{
    /// <summary>The fixed namespace of zone ids (never change it: existing zones would no longer resolve).</summary>
    public static readonly Guid Namespace = new("6f1c2d7e-4b0a-4c1e-9a53-8d2f0e7b3c61");

    public static Guid For(Guid ownerNodeId, string slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var name = Encoding.UTF8.GetBytes(ownerNodeId.ToString("D") + "/" + slot);
        var space = Namespace.ToByteArray(bigEndian: true);
        var input = new byte[space.Length + name.Length];
        space.CopyTo(input, 0);
        name.CopyTo(input, space.Length);

        var hash = SHA1.HashData(input);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}
