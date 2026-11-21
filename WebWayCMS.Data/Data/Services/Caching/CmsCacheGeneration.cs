namespace WebWayCMS.Data.Services.Caching;

/// <inheritdoc />
public class CmsCacheGeneration : ICmsCacheGeneration
{
    private long _current;
    private long _localEpoch;

    public long Current => Interlocked.Read(ref _current);

    public string Key => $"{Current}.{Interlocked.Read(ref _localEpoch)}";

    public event Action<long>? Changed;

    public void Advance(long observed)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _current);
            if (observed <= current)
                return;
            // Only the thread whose compare-and-swap moved the value raises: two threads racing to
            // the same value can't both move it from the same starting point.
            if (TryMoveTo(current, observed))
                break;
        }
        Changed?.Invoke(observed);
    }

    public void AdvanceLocal()
    {
        Interlocked.Increment(ref _localEpoch);
        Changed?.Invoke(Current);
    }

    /// <summary>
    /// The forward-only commit: moves <see cref="_current"/> from <paramref name="expected"/> to
    /// <paramref name="next"/> unless another thread moved it first (then the caller retries).
    /// </summary>
    protected virtual bool TryMoveTo(long expected, long next)
        => Interlocked.CompareExchange(ref _current, next, expected) == expected;
}
