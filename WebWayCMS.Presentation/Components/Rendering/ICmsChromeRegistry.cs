namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Resolves the host-provided public-site chrome component (header / navigation / footer), discovered
/// by convention from components decorated with <see cref="WebWayCMS.Attributes.CmsChromeAttribute"/>.
/// </summary>
public interface ICmsChromeRegistry
{
    /// <summary>
    /// The chrome component type to wrap public page content with, or <c>null</c> when the host
    /// provides no chrome (the page body then renders directly inside the document shell).
    /// </summary>
    Type? ChromeType { get; }
}
