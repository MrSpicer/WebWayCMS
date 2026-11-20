using NUnit.Framework;

using WebWayCMS.Data.Services.Registrations;

namespace WebWayCMS.Data.Tests;

[TestFixture]
public class RegistrationSynchronizerTests
{
    private sealed class Row
    {
        public string Key { get; set; } = string.Empty;
        public string CodeValue { get; set; } = string.Empty;
        public string AdminValue { get; set; } = string.Empty;
    }

    private sealed class FakeTarget : IRegistrationSyncTarget<Row>
    {
        public List<Row> Rows { get; } = [];
        public List<Row> Inserted { get; } = [];
        public int Saves { get; private set; }
        public Func<Row, (bool, string?)> Insert { get; set; } = _ => (true, null);

        public string KeyOf(Row row) => row.Key;

        public Task<IReadOnlyList<Row>> LoadRowsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Row>>(Rows.ToList());

        public Task SaveCodeOwnedChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task<(bool Success, string? ErrorMessage)> InsertAsync(Row row, CancellationToken ct = default)
        {
            var result = Insert(row);
            if (result.Item1)
                Inserted.Add(row);
            return Task.FromResult(result);
        }
    }

    private static RegistrationSyncItem<Row> Item(string key, string codeValue)
        => new(
            key,
            () => new Row { Key = key, CodeValue = codeValue, AdminValue = "default" },
            row =>
            {
                if (row.CodeValue == codeValue)
                    return false;
                row.CodeValue = codeValue;
                return true;
            });

    [Test]
    public async Task SyncAsync_InsertsMissing_SyncsCodeOwnedOnEveryRow_KeepsAdminValues_ReportsOrphans()
    {
        var target = new FakeTarget();
        var v1 = new Row { Key = "Pages", CodeValue = "old", AdminValue = "edited" };
        var v2 = new Row { Key = "pages", CodeValue = "old", AdminValue = "edited" };
        var current = new Row { Key = "widgets", CodeValue = "same", AdminValue = "edited" };
        var orphan = new Row { Key = "gone", CodeValue = "x" };
        target.Rows.AddRange([v1, v2, current, orphan]);

        var result = await RegistrationSynchronizer.SyncAsync(target, [
            Item("pages", "new"),
            Item("widgets", "same"),
            Item("articles", "a"),
            Item("ARTICLES", "duplicate declaration is ignored"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Inserted, Is.EqualTo(new[] { "articles" }));
            Assert.That(result.Updated, Is.EqualTo(new[] { "pages" }));
            Assert.That(result.Failed, Is.Empty);
            Assert.That(result.Orphaned, Is.EqualTo(new[] { "gone" }));
            Assert.That(v1.CodeValue, Is.EqualTo("new"));
            Assert.That(v2.CodeValue, Is.EqualTo("new"));
            Assert.That(v1.AdminValue, Is.EqualTo("edited"));
            Assert.That(target.Saves, Is.EqualTo(1));
            Assert.That(target.Inserted.Single().CodeValue, Is.EqualTo("a"));
        });
    }

    [Test]
    public async Task SyncAsync_NothingChanged_DoesNotSave()
    {
        var target = new FakeTarget();
        target.Rows.Add(new Row { Key = "pages", CodeValue = "same" });

        var result = await RegistrationSynchronizer.SyncAsync(target, [Item("pages", "same")]);

        Assert.Multiple(() =>
        {
            Assert.That(target.Saves, Is.Zero);
            Assert.That(result.Updated, Is.Empty);
            Assert.That(result.Inserted, Is.Empty);
            Assert.That(result.Orphaned, Is.Empty);
        });
    }

    [Test]
    public async Task SyncAsync_FailedInserts_AreReported_AndTheRestContinue()
    {
        var target = new FakeTarget
        {
            Insert = row => row.Key switch
            {
                "refused" => (false, "Name taken."),
                "silent" => (false, null),
                "throws" => throw new InvalidOperationException("Boom."),
                _ => (true, null),
            },
        };

        var result = await RegistrationSynchronizer.SyncAsync(target, [
            Item("refused", "a"), Item("silent", "b"), Item("throws", "c"), Item("ok", "d"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Inserted, Is.EqualTo(new[] { "ok" }));
            Assert.That(result.Failed, Is.EqualTo(new[]
            {
                new RegistrationSyncFailure("refused", "Name taken."),
                new RegistrationSyncFailure("silent", "The registration could not be saved."),
                new RegistrationSyncFailure("throws", "Boom."),
            }));
        });
    }

    [Test]
    public void SyncAsync_NullArguments_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => RegistrationSynchronizer.SyncAsync<Row>(null!, []), Throws.ArgumentNullException);
            Assert.That(() => RegistrationSynchronizer.SyncAsync(new FakeTarget(), null!), Throws.ArgumentNullException);
        });
    }
}
