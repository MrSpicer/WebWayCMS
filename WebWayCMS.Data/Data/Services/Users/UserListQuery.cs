namespace WebWayCMS.Data.Services.Users;

/// <summary>One page of the user list. <see cref="Status"/> null lists every user that is not soft-deleted.</summary>
public sealed record UserListQuery(string? Role = null, UserStatus? Status = null, int Skip = 0, int Take = UserListQuery.DefaultTake)
{
    public const int DefaultTake = 25;
    public const int MaxTake = 200;
}
