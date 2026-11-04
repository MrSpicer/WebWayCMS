using NUnit.Framework;

using WebWayCMS.Models.ContentZone;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class SlotRemapPlannerTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        // Same names keep their content; the rest pair by position; leftovers go to the last new slot.
        yield return new TestCaseData(new[] { "Hero", "Column1", "Column2", "Column3" }, new[] { "Left", "Right" },
                new[] { "Hero", "Column1", "Column2", "Column3" },
                new[] { "Hero>Left", "Column1>Right", "Column2>Right", "Column3>Right" })
            .SetName("Plan_FewerSlots_LeftoversGoToTheLastSlot");
        yield return new TestCaseData(new[] { "Left", "Right" }, new[] { "Column1", "Column2", "Column3" },
                new[] { "Left", "Right" }, new[] { "Left>Column1", "Right>Column2" })
            .SetName("Plan_MoreSlots_FillsTheFirstOnes");
        yield return new TestCaseData(new[] { "Column1", "Column2", "Column3" }, new[] { "Column1", "Column2", "Column3", "Column4" },
                new[] { "Column1", "Column2", "Column3" }, Array.Empty<string>())
            .SetName("Plan_SharedNames_MoveNothing");
        yield return new TestCaseData(new[] { "Main", "Sidebar" }, new[] { "Sidebar", "Main" },
                new[] { "Main", "Sidebar" }, Array.Empty<string>())
            .SetName("Plan_SwappedOrder_KeepsNames");
        yield return new TestCaseData(new[] { "Main", "Sidebar" }, new[] { "Header", "Main", "Footer" },
                new[] { "Main", "Sidebar" }, new[] { "Sidebar>Header" })
            .SetName("Plan_MixedSharedAndNew");
        yield return new TestCaseData(new[] { "Left", "Right" }, new[] { "Column1", "Column2" },
                new[] { "Right" }, new[] { "Right>Column2" })
            .SetName("Plan_OnlySlotsWithContentMove");
        yield return new TestCaseData(new[] { "Left", "Left", "Right" }, new[] { "Main", "Main" },
                new[] { "Left", "Right" }, new[] { "Left>Main", "Right>Main" })
            .SetName("Plan_DuplicatesAreIgnored");
        yield return new TestCaseData(new[] { "Left" }, Array.Empty<string>(), new[] { "Left" }, Array.Empty<string>())
            .SetName("Plan_NewViewWithoutSlots_MovesNothing");
    }

    [TestCaseSource(nameof(Cases))]
    public void Plan(string[] oldSlots, string[] newSlots, string[] withContent, string[] expected)
    {
        var moves = SlotRemapPlanner.Plan(oldSlots, newSlots, withContent);

        Assert.That(moves.Select(m => $"{m.FromSlot}>{m.ToSlot}"), Is.EqualTo(expected));
    }
}
