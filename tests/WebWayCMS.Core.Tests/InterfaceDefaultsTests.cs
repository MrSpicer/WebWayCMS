using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using NUnit.Framework;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// Minimal implementations that keep the interface default methods, so those default bodies
/// (defined in WebWayCMS.Core) are executed and counted.
/// </summary>
internal sealed class MinimalHandler : IAdminCrudHandler
{
	public string ContentType => "minimal";
	public string DisplayName => "Minimal";
	public string[]? WriteRoles => null;
	public string IndexViewPath => "i";
	public string UpsertViewPath => "u";
	public Task<object> GetIndexViewModelAsync(CancellationToken ct = default) => Task.FromResult<object>("index");
	public Task<object?> GetUpsertViewModelAsync(Guid? id, IQueryCollection query, CancellationToken ct = default) => Task.FromResult<object?>(null);
	public object CreateEmptyUpsertViewModel() => new object();
	public Task<AdminSaveResult> SaveUpsertAsync(object model, CancellationToken ct = default) => Task.FromResult(new AdminSaveResult(true));
	public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
	public Task<IEnumerable<object>> GetApiListAsync(CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
	public bool HasSecondaryApiList => false;
	public Task<IEnumerable<object>> GetSecondaryApiListAsync(string key, CancellationToken ct = default) => Task.FromResult(Enumerable.Empty<object>());
	public IAdminRegistryHandler? RegistryHandler => null;
	public IAdminCrudChildHandler? ChildHandler => null;
}

internal sealed class MinimalChild : IAdminCrudChildHandler
{
	public string ChildType => "minimal";
	public string ChildDisplayName => "Minimal";
	public string[]? WriteRoles => null;
	public string ChildIndexViewPath => "i";
	public string ChildUpsertViewPath => "u";
	public Task<object?> GetChildIndexViewModelAsync(string parentKey, CancellationToken ct = default) => Task.FromResult<object?>(null);
	public Task<object?> GetChildUpsertViewModelAsync(string parentKey, Guid? id, CancellationToken ct = default) => Task.FromResult<object?>(null);
	public Task SetChildUpsertViewDataAsync(ViewDataDictionary viewData, string parentKey, CancellationToken ct = default) => Task.CompletedTask;
	public object CreateEmptyChildUpsertViewModel() => new object();
	public Task<AdminSaveResult> SaveChildUpsertAsync(string parentKey, object model, CancellationToken ct = default) => Task.FromResult(new AdminSaveResult(true));
	public Task<bool> DeleteChildAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
	public bool SupportsReorder => false;
	public Task<bool> ReorderAsync(string parentKey, List<Guid> orderedIds, CancellationToken ct = default) => Task.FromResult(false);
}

[TestFixture]
public class InterfaceDefaultsTests
{
	[Test]
	public async Task IAdminCrudHandler_DefaultMembers()
	{
		IAdminCrudHandler handler = new MinimalHandler();
		var query = new MvcHarness().NewHttpContext(Array.Empty<string>()).Request.Query;

		Assert.Multiple(async () =>
		{
			Assert.That(await handler.GetIndexViewModelAsync(query), Is.EqualTo("index"));
			Assert.That(handler.SupportsVersionHistory, Is.False);
			Assert.That(await handler.GetVersionHistoryViewModelAsync(Guid.NewGuid()), Is.Null);
			Assert.That(await handler.GetRestoreVersionViewModelAsync(Guid.NewGuid()), Is.Null);
			Assert.That(await handler.DeleteVersionAsync(Guid.NewGuid()), Is.False);
		});
	}

	[Test]
	public async Task IAdminCrudChildHandler_DefaultMembers()
	{
		IAdminCrudChildHandler child = new MinimalChild();

		Assert.Multiple(async () =>
		{
			Assert.That(child.SupportsVersionHistory, Is.False);
			Assert.That(await child.GetChildVersionHistoryViewModelAsync("k", Guid.NewGuid()), Is.Null);
			Assert.That(await child.GetChildRestoreVersionViewModelAsync("k", Guid.NewGuid()), Is.Null);
			Assert.That(await child.DeleteChildVersionAsync(Guid.NewGuid()), Is.False);
		});
	}
}
