namespace WebWayCMS.Data.Services.Caching;

/// <summary>
/// This process's current view of the cross-host cache generation. <see cref="Current"/> mirrors the
/// shared database stamp and moves forward only: <see cref="CmsCacheGeneration.Advance"/> raises
/// <see cref="Changed"/> once per new value, whether the value arrived through the poller (another
/// host bumped it) or through the local bump (this host published something). When the database bump
/// fails, <see cref="AdvanceLocal"/> evicts this host without claiming a stamp value the database
/// never issued, so the next real bump from any host is still seen.
/// </summary>
public interface ICmsCacheGeneration
{
    /// <summary>The highest database stamp this process has observed (0 before the first observation).</summary>
    long Current { get; }

    /// <summary>
    /// The cache-key form of the generation: the database stamp plus this host's local epoch (moved by
    /// <see cref="AdvanceLocal"/>). Cache keys and output-cache vary values use this, never <see cref="Current"/>.
    /// </summary>
    string Key { get; }

    /// <summary>Raised after <see cref="Key"/> has moved, with <see cref="Current"/>. Subscribers must not throw.</summary>
    event Action<long>? Changed;

    /// <summary>Moves <see cref="Current"/> forward to <paramref name="observed"/> if it is newer; raises <see cref="Changed"/>.</summary>
    void Advance(long observed);

    /// <summary>Moves only this host's local epoch (the shared stamp could not be bumped); raises <see cref="Changed"/>.</summary>
    void AdvanceLocal();
}
