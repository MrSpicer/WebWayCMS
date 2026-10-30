using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using WebWayCMS.Data.Services;
using WebWayCMS.Models.PageHistory;

namespace WebWayCMS.Mcp;

/// <summary>
/// MCP tools for a page's page-set history: every publish of the page together with all of its zone
/// items. Mirrors <c>AdminPageHistoryController</c>.
/// </summary>
[McpServerToolType]
public sealed class PageHistoryToolset
{
    private readonly IPageHistoryModel _model;
    private readonly IMcpCaller _caller;

    public PageHistoryToolset(IPageHistoryModel model, IMcpCaller caller)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
    }

    [McpServerTool(Name = "list_page_history", ReadOnly = true, OpenWorld = false)]
    [Description("Lists a page's history entries, newest first. An entry is recorded after every change to the page or its zone items (kind Draft, with a note such as \"Added ContentBlock to Main\") and after every publish (kind Published: what went live). Each holds the page and every active zone item, nested zones included. Paged: totalCount is the page's entry count, hasOlder says whether to request the next skip.")]
    public async Task<PageHistoryIndexViewModel> ListPageHistory(
        [Description("The page's nodeId.")] Guid pageNodeId,
        [Description("How many of the newest entries to skip (default 0).")] int skip = 0,
        [Description("How many entries to return (default 50, max 200).")] int take = IPageHistoryService.DefaultPageSize,
        CancellationToken ct = default)
        => await _model.GetIndexAsync(pageNodeId, skip, take, ct)
           ?? throw new McpException($"No page found with nodeId '{pageNodeId}'.");

    [McpServerTool(Name = "get_page_history_entry", ReadOnly = true, OpenWorld = false)]
    [Description("Gets one page history entry. By default its zone items and how they changed relative to the previous entry. With compareWithCurrent, how the page's current drafts changed since the entry (what revert_page_to_entry would undo) plus the stateToken that revert_page_to_entry requires.")]
    public async Task<object> GetPageHistoryEntry(
        [Description("The page's nodeId.")] Guid pageNodeId,
        [Description("The history entry id (from list_page_history).")] Guid entryId,
        [Description("Compare the entry with the page's current drafts instead of with the previous entry.")] bool compareWithCurrent = false,
        CancellationToken ct = default)
    {
        object? vm = compareWithCurrent
            ? await _model.GetCompareAsync(pageNodeId, entryId, ct)
            : await _model.GetEntryAsync(pageNodeId, entryId, ct);
        return vm ?? throw new McpException($"No history entry '{entryId}' found for page '{pageNodeId}'.");
    }

    [McpServerTool(Name = "revert_page_to_entry", Destructive = false, OpenWorld = false)]
    [Description("Reverts a page and all of its zone items to a history entry, as drafts (nothing goes live until publish_content publishes the page). Zone items added since the entry become inactive drafts; nothing is deleted. Requires the stateToken from get_page_history_entry with compareWithCurrent=true; it is refused if the page changed since.")]
    public async Task<McpRevertResult> RevertPageToEntry(
        [Description("The page's nodeId.")] Guid pageNodeId,
        [Description("The history entry id.")] Guid entryId,
        [Description("The stateToken from get_page_history_entry (compareWithCurrent=true).")] string stateToken,
        CancellationToken ct = default)
    {
        // AdminPageHistoryController is Admin-only.
        McpToolHelpers.RequireAdmin(_caller);
        var (success, error) = await _model.RevertAsync(pageNodeId, entryId, stateToken, ct);
        if (!success)
            throw new McpException(error!);
        return new McpRevertResult(true);
    }

    [McpServerTool(Name = "delete_page_history_entry", OpenWorld = false)] // Destructive left at its true default: permanently deletes a history entry
    [Description("Permanently deletes a page history entry. The versions it captured are kept (and can then be deleted on their own).")]
    public async Task<McpDeleteResult> DeletePageHistoryEntry(
        [Description("The page's nodeId.")] Guid pageNodeId,
        [Description("The history entry id.")] Guid entryId,
        CancellationToken ct = default)
    {
        McpToolHelpers.RequireAdmin(_caller);
        if (!await _model.DeleteAsync(pageNodeId, entryId, ct))
            throw new McpException($"No history entry '{entryId}' found for page '{pageNodeId}'.");
        return new McpDeleteResult(true);
    }
}
