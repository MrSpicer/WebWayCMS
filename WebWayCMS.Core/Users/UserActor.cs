namespace WebWayCMS.Users;

/// <summary>
/// Who is acting. <see cref="RequirePassword"/> is true for the admin UI, which asks for the acting Admin's
/// password (<see cref="Password"/>) before deleting, purging or granting Admin; the MCP tools act with the
/// token's authentication and pass false.
/// </summary>
public sealed record UserActor(string UserId, string? Password = null, bool RequirePassword = true);
