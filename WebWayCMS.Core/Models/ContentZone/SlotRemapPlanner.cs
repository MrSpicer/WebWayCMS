using WebWayCMS.Data.Services;

namespace WebWayCMS.Models.ContentZone;

/// <summary>
/// Maps a container's old view's slots onto its new view's, so a view change never strands content:
/// a slot whose name exists in both keeps its content; the remaining old slots pair with the remaining
/// new ones by position (render order); any old slot left over goes to the new view's last slot.
/// </summary>
public static class SlotRemapPlanner
{
    /// <summary>
    /// The moves for the old slots that hold content (<paramref name="slotsWithContent"/>); empty when the new
    /// view has no slots or nothing needs to move.
    /// </summary>
    public static IReadOnlyList<SlotMove> Plan(
        IEnumerable<string> oldSlots, IEnumerable<string> newSlots, IEnumerable<string> slotsWithContent)
    {
        var oldList = oldSlots.Distinct(StringComparer.Ordinal).ToList();
        var newList = newSlots.Distinct(StringComparer.Ordinal).ToList();
        if (newList.Count == 0)
            return [];

        var withContent = slotsWithContent.ToHashSet(StringComparer.Ordinal);
        var unmatchedOld = oldList.Where(s => !newList.Contains(s, StringComparer.Ordinal)).ToList();
        var unmatchedNew = newList.Where(s => !oldList.Contains(s, StringComparer.Ordinal)).ToList();

        var moves = new List<SlotMove>();
        for (var i = 0; i < unmatchedOld.Count; i++)
        {
            var target = i < unmatchedNew.Count ? unmatchedNew[i] : newList[^1];
            if (withContent.Contains(unmatchedOld[i]))
                moves.Add(new SlotMove(unmatchedOld[i], target));
        }

        return moves;
    }
}
