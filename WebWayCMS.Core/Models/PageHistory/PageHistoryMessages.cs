using WebWayCMS.Data.Models;

namespace WebWayCMS.Models.PageHistory;

/// <summary>User-facing messages shared by the page-history admin UI, the handlers and MCP.</summary>
public static class PageHistoryMessages
{
    /// <summary>Why a version delete was refused: a page-history entry still captures that version.</summary>
    public static string VersionInHistory(PageSetSnapshot snapshot)
        => $"This version is part of the page history entry from {snapshot.CreatedUtc:yyyy-MM-dd HH:mm} UTC. Delete that history entry first.";
}
