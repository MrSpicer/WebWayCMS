namespace WebWayCMS.Mcp;

/// <summary>One stored media blob, as reported by <c>list_media</c>.</summary>
public sealed record McpMediaInfo(
    string Hash,
    string ContentType,
    long ByteLength,
    int Width,
    int Height,
    string OriginalFileName,
    DateTime CreatedUtc,
    string Url);
