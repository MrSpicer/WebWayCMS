using System.ComponentModel;

using ModelContextProtocol.Server;

using WebWayCMS.Data.Services;
using WebWayCMS.Media;

namespace WebWayCMS.Mcp;

/// <summary>
/// Read-only MCP tools for the media library.
/// </summary>
/// <remarks>
/// Deliberately read-only. Image bytes do not travel through tool calls: an agent authoring an
/// image content item sets its <c>blobHash</c> field to a blob that already exists, and this tool is
/// how it discovers one. Uploading is an admin-UI (or content-seed) operation.
/// </remarks>
[McpServerToolType]
public sealed class MediaToolset
{
    private readonly IMediaLibrary _mediaLibrary;

    public MediaToolset(IMediaLibrary mediaLibrary)
    {
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    }

    [McpServerTool(Name = "list_media", ReadOnly = true, OpenWorld = false)]
    [Description("Lists stored images in the CMS media library, newest first. Use a returned \"hash\" as the blobHash field when creating or updating an image content item; bytes cannot be uploaded over MCP.")]
    public async Task<IReadOnlyList<McpMediaInfo>> ListMedia(
        [Description("How many to skip (default 0).")] int skip = 0,
        [Description("How many to return (default 50).")] int take = 50,
        CancellationToken ct = default)
    {
        var blobs = await _mediaLibrary.ListAsync(skip, take, ct);
        return blobs
            .Select(b => new McpMediaInfo(
                b.Hash, b.ContentType, b.ByteLength, b.Width, b.Height,
                b.OriginalFileName, b.CreatedUtc, MediaUrl.For(b.Hash, b.OriginalFileName)))
            .ToList();
    }
}
