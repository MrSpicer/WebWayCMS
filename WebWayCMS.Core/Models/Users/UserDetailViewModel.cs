using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Models.Users;

/// <summary>The Users widget's management screen for one user.</summary>
public sealed class UserDetailViewModel
{
    /// <summary>The user, or null when the id names no user (the view says so).</summary>
    public UserDetail? User { get; init; }

    public IReadOnlyList<RoleSummary> Roles { get; init; } = [];

    /// <summary>Back to the list; empty when the widget is a lone Single item with no list.</summary>
    public string ListUrl { get; init; } = string.Empty;

    public string CurrentUrl { get; init; } = string.Empty;

    /// <summary>The acting Admin is looking at their own account (self-destructive actions are hidden).</summary>
    public bool IsSelf { get; init; }
}
