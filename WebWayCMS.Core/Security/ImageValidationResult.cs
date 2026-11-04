namespace WebWayCMS.Security;

/// <summary>
/// The outcome of validating an uploaded image. <see cref="ContentType"/> is the type sniffed from
/// the file's own bytes, which is what callers should store — never the client-declared header.
/// </summary>
public readonly record struct ImageValidationResult(
    bool Success,
    string? ErrorMessage,
    string? ContentType,
    int Width,
    int Height);
