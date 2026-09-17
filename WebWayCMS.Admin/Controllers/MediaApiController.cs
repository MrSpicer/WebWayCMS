using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using WebWayCMS.Data.Services;
using WebWayCMS.Media;
using WebWayCMS.Security;

namespace WebWayCMS.Controllers.Api;

/// <summary>
/// Upload and management API for the media library. Used by the admin image picker.
/// </summary>
/// <remarks>
/// Deliberately thin: validation lives in <see cref="ImageValidator"/> and storage in
/// <see cref="IMediaLibrary"/>, both of which sit in coverage-gated projects.
/// </remarks>
[ApiController]
[Route("api/media")]
[Authorize(Roles = "Admin")]
public class MediaApiController : ControllerBase
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<MediaApiController>();

    private readonly IMediaLibrary _mediaLibrary;
    private readonly MediaOptions _options;

    public MediaApiController(IMediaLibrary mediaLibrary, IOptions<MediaOptions> options)
    {
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Uploads one image and returns its content hash.</summary>
    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MediaOptions.DefaultMaxUploadBytes + 4096)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Choose a file to upload." });

        if (file.Length > _options.MaxUploadBytes)
            return BadRequest(new { error = $"The image is larger than the {_options.MaxUploadBytes / (1024 * 1024)} MB upload limit." });

        try
        {
            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, ct);
                bytes = buffer.ToArray();
            }

            // The declared content type and the file extension are both client-supplied; the
            // validator decides what this actually is by reading the bytes.
            var validation = ImageValidator.Validate(bytes, _options.MaxUploadBytes, _options.AllowedContentTypes);
            if (!validation.Success)
                return BadRequest(new { error = validation.ErrorMessage });

            var result = await _mediaLibrary.AddAsync(
                bytes, validation.ContentType!, file.FileName, validation.Width, validation.Height, ct);

            if (!result.Success || result.Blob == null)
                return BadRequest(new { error = result.ErrorMessage ?? "The image could not be stored." });

            var blob = result.Blob;
            return Ok(new
            {
                hash = blob.Hash,
                contentType = blob.ContentType,
                width = blob.Width,
                height = blob.Height,
                byteLength = blob.ByteLength,
                originalFileName = blob.OriginalFileName,
                url = MediaUrl.For(blob.Hash, blob.OriginalFileName)
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to upload media file {FileName}", file.FileName);
            return StatusCode(500, new { error = "The image could not be uploaded." });
        }
    }

    /// <summary>Lists catalogued media, newest first. Metadata only — never bytes.</summary>
    [HttpGet("list")]
    public async Task<IActionResult> List(int skip = 0, int take = 100, CancellationToken ct = default)
    {
        try
        {
            var blobs = await _mediaLibrary.ListAsync(skip, take, ct);
            return Ok(blobs.Select(b => new
            {
                hash = b.Hash,
                contentType = b.ContentType,
                width = b.Width,
                height = b.Height,
                byteLength = b.ByteLength,
                originalFileName = b.OriginalFileName,
                createdUtc = b.CreatedUtc,
                url = MediaUrl.For(b.Hash, b.OriginalFileName)
            }));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to list media.");
            return StatusCode(500, new { error = "The media library could not be listed." });
        }
    }

    /// <summary>Permanently removes a blob. Refused while any image version still references it.</summary>
    [HttpDelete("{hash}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string hash, CancellationToken ct)
    {
        try
        {
            var (success, error) = await _mediaLibrary.DeleteAsync(hash, ct);
            return success ? Ok(new { success = true }) : BadRequest(new { error });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to delete media blob {Hash}", hash);
            return StatusCode(500, new { error = "The image could not be deleted." });
        }
    }
}
