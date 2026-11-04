namespace WebWayCMS.Data.Services.Users;

public sealed record UserListPage(IReadOnlyList<UserListItem> Items, int Total, int Skip, int Take);
