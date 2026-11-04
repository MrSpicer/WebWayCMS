namespace WebWayCMS.Data.Services.Users;

/// <summary>Profile fields an Admin edits (and an invite starts with). Blank values are stored as null.</summary>
public sealed record UserProfileInput(string? DisplayName, string? FirstName, string? LastName);
