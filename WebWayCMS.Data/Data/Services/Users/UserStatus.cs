using System.Text.Json.Serialization;

namespace WebWayCMS.Data.Services.Users;

/// <summary>A user's account state, in the order it is decided (a deleted user is also locked, and so on).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserStatus>))]
public enum UserStatus
{
    Active,
    Unconfirmed,
    Locked,
    Deleted,
}
