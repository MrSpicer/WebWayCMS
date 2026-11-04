namespace WebWayCMS.Data.Services;

/// <summary>Moves the content of a container's <paramref name="FromSlot"/> zone into its <paramref name="ToSlot"/> zone.</summary>
public sealed record SlotMove(string FromSlot, string ToSlot);
