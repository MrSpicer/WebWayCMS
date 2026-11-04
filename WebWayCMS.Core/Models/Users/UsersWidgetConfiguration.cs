using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Models.Users;

/// <summary>
/// Configuration for the Users widget. <b>List</b> shows the user table (role / status filters, paging, the
/// Invite form) and owns the <c>edit/{userId:guid}</c> route, where it shows that user's management screen;
/// <b>Single</b> shows the route's user, else <see cref="UserId"/>.
/// </summary>
public class UsersWidgetConfiguration : WidgetConfiguration
{
    public const string GuidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    [FormProperty(
        Label = "User ID",
        HelpText = "The user to manage when the URL does not name one. Leave empty to show only users opened from a list.",
        DisplayModes = WidgetDisplayMode.Single,
        Pattern = GuidPattern,
        PatternErrorMessage = "Enter a user ID (a GUID).",
        Order = 1
    )]
    public string? UserId { get; set; }

    [FormProperty(
        Label = "Page Size",
        HelpText = "Users per page (1-200). Leave empty for 25.",
        EditorType = EditorType.Number,
        Min = 1,
        Max = UserListQuery.MaxTake,
        DisplayModes = WidgetDisplayMode.List,
        Order = 1,
        FormComponent = "Number"
    )]
    public int? PageSize { get; set; }

    [FormProperty(
        Label = "Show Roles",
        HelpText = "Show each user's roles in the list.",
        EditorType = EditorType.Checkbox,
        DisplayModes = WidgetDisplayMode.List,
        Order = 2,
        FormComponent = "Checkbox"
    )]
    public bool ShowRoles { get; set; } = true;

    [FormProperty(
        Label = "Show Status",
        HelpText = "Show each user's account status in the list.",
        EditorType = EditorType.Checkbox,
        DisplayModes = WidgetDisplayMode.List,
        Order = 3,
        FormComponent = "Checkbox"
    )]
    public bool ShowStatus { get; set; } = true;

    [FormProperty(
        Label = "Show Last Login",
        HelpText = "Show when each user last signed in.",
        EditorType = EditorType.Checkbox,
        DisplayModes = WidgetDisplayMode.List,
        Order = 4,
        FormComponent = "Checkbox"
    )]
    public bool ShowLastLogin { get; set; } = true;

    [FormProperty(
        Label = "Show Invite Form",
        HelpText = "Offer the Invite user form above the list.",
        EditorType = EditorType.Checkbox,
        DisplayModes = WidgetDisplayMode.List,
        Order = 5,
        FormComponent = "Checkbox"
    )]
    public bool ShowInvite { get; set; } = true;

    /// <summary>The configured page size, defaulted and clamped.</summary>
    public int EffectivePageSize => Math.Clamp(PageSize is > 0 ? PageSize.Value : UserListQuery.DefaultTake, 1, UserListQuery.MaxTake);
}
