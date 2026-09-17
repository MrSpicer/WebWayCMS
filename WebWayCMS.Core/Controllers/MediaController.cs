using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using WebWayCMS.Attributes;

using WebWayCMS.Data.Services;

namespace WebWayCMS.Controllers;

/// <summary>
/// Serves stored media bytes at a content-addressed URL.
/// </summary>
/// <remarks>
/// Lives in the core library rather than the admin one so that a rendering-only host serves images
/// too. The address is the blob's SHA-256, so the bytes behind a URL can never change — which is
/// what makes an immutable, effectively permanent cache entry safe here.
/// <para>
/// A fixed literal path does not collide with the CMS route table: attribute-routed controllers are
/// mapped before the dynamic catch-all, and a literal segment outranks a catch-all regardless.
/// </para>
/// </remarks>
[AllowAnonymous]
// Occupies the pattern in the CMS route table so an editor cannot create a page at /media that
// would silently never resolve. Reserved rows are never matched for dispatch.
[CmsRoute("/media", Action = "Get", IsReserved = true)]
[Route("media")]
public sealed class MediaController : Controller
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext<MediaController>();
    private readonly IMediaLibrary _mediaLibrary;

    internal const string CacheControlValue = "public, max-age=31536000, immutable";

    public MediaController(IMediaLibrary mediaLibrary)
    {
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    }

    /// <summary>
    /// Returns the bytes for <paramref name="hash"/>. The trailing file name is cosmetic and
    /// ignored — the hash alone identifies the blob.
    /// </summary>
    [HttpGet("{hash}/{fileName?}")]
    public async Task<IActionResult> Get(string hash, string? fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return NotFound();

        var blob = await _mediaLibrary.GetMetadataAsync(hash, ct);
        if (blob == null)
            return NotFound();

        var etag = $"\"{blob.Hash}\"";

        // Content-addressed, so a matching ETag is proof the client's copy is current.
        var ifNoneMatch = Request.Headers.IfNoneMatch;
        if (ifNoneMatch.Count > 0 && ifNoneMatch.Any(v => v == etag || v == "*"))
        {
            ApplyCacheHeaders(etag);
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var bytes = await _mediaLibrary.GetBytesAsync(hash, ct);
        if (bytes == null)
        {
            // Catalogued but no bytes: the configured blob store has lost them, or a store swap
            // left this blob behind. Worth a log line — it is a storage fault, not a bad request.
            _logger.Warning("Media blob {Hash} is catalogued but missing from the blob store.", hash);
            return NotFound();
        }

        ApplyCacheHeaders(etag);
        return File(bytes, blob.ContentType);
    }

    private void ApplyCacheHeaders(string etag)
    {
        Response.Headers[HeaderNames.CacheControl] = CacheControlValue;
        Response.Headers[HeaderNames.ETag] = etag;
    }
}
