using FluentAssertions;
using PcsRemote.Core;
using PcsRemote.Web.Services;

namespace PcsRemote.Web.Tests.Services;

[TestClass]
public class AutomationLogServiceTests
{
    // ──────────────────────────────────────────────────────────────────────
    // TC-1  AddEntry stores entry retrievable via GetRecentEntries
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddEntry_StoresEntry_RetrievableViaGetRecentEntries()
    {
        var sut = new AutomationLogService();

        sut.AddEntry("Launching PCS Pro", AutomationLogOutcome.Info);

        var entries = sut.GetRecentEntries();
        entries.Should().ContainSingle();
        entries[0].Action.Should().Be("Launching PCS Pro");
        entries[0].Outcome.Should().Be(AutomationLogOutcome.Info);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  Entries ordered oldest-to-newest
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetRecentEntries_ReturnsEntriesInOldestToNewestOrder()
    {
        var sut = new AutomationLogService();

        sut.AddEntry("A", AutomationLogOutcome.Info);
        sut.AddEntry("B", AutomationLogOutcome.Success);
        sut.AddEntry("C", AutomationLogOutcome.Failure);

        var entries = sut.GetRecentEntries();
        entries.Should().HaveCount(3);
        entries[0].Action.Should().Be("A");
        entries[1].Action.Should().Be("B");
        entries[2].Action.Should().Be("C");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  Buffer evicts oldest at capacity
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddEntry_AtCapacity_EvictsOldestEntry()
    {
        var sut = new AutomationLogService();

        for (int i = 0; i < 201; i++)
        {
            sut.AddEntry($"Entry-{i}", AutomationLogOutcome.Info);
        }

        var entries = sut.GetRecentEntries();
        entries.Should().HaveCount(200);
        entries[0].Action.Should().Be("Entry-1");
        entries[199].Action.Should().Be("Entry-200");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  EntryAdded fires after commit (publish-after-commit)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddEntry_EntryAddedEventFires_EntryVisibleInGetRecentEntries()
    {
        var sut = new AutomationLogService();
        IReadOnlyList<AutomationLogEntry>? snapshotDuringEvent = null;

        sut.EntryAdded += (_, _) =>
        {
            snapshotDuringEvent = sut.GetRecentEntries();
        };

        sut.AddEntry("Test action", AutomationLogOutcome.Success);

        snapshotDuringEvent.Should().NotBeNull();
        snapshotDuringEvent!.Should().ContainSingle()
            .Which.Action.Should().Be("Test action");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  Snapshot is immutable
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetRecentEntries_ReturnsImmutableSnapshot()
    {
        var sut = new AutomationLogService();
        sut.AddEntry("First", AutomationLogOutcome.Info);

        var snapshot = sut.GetRecentEntries();

        sut.AddEntry("Second", AutomationLogOutcome.Info);

        snapshot.Should().ContainSingle()
            .Which.Action.Should().Be("First");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6  Timestamp is generated internally
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddEntry_GeneratesTimestampCloseToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var sut = new AutomationLogService();

        sut.AddEntry("Timed action", AutomationLogOutcome.Info);

        var after = DateTimeOffset.UtcNow;
        var entry = sut.GetRecentEntries().Single();
        entry.Timestamp.Should().BeOnOrAfter(before);
        entry.Timestamp.Should().BeOnOrBefore(after);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7  Thread-safe concurrent adds
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddEntry_ConcurrentAdds_AllEntriesStored()
    {
        var sut = new AutomationLogService();

        Parallel.For(0, 100, i =>
        {
            sut.AddEntry($"Concurrent-{i}", AutomationLogOutcome.Info);
        });

        sut.GetRecentEntries().Should().HaveCount(100);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-12  Concurrent adds exceeding capacity
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddEntry_ConcurrentAddsExceedingCapacity_BufferCappedAt200()
    {
        var sut = new AutomationLogService();

        Parallel.For(0, 300, i =>
        {
            sut.AddEntry($"Overflow-{i}", AutomationLogOutcome.Info);
        });

        var entries = sut.GetRecentEntries();
        entries.Should().HaveCount(200);
    }
}
