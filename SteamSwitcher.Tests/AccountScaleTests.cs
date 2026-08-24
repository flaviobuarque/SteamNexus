using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Helpers;
using SteamSwitcher.ViewModels;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SteamSwitcher.Tests;

public sealed class AccountScaleTests(ITestOutputHelper output)
{
    public static TheoryData<int> ScaleSizes => new() { 100, 500, 1_000, 5_000 };

    [Theory]
    [MemberData(nameof(ScaleSizes))]
    public void ParseSortAndSearch_ScalesLinearly(int count)
    {
        var bytes = Encoding.UTF8.GetBytes(CreateLoginUsersVdf(count));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        using var stream = new MemoryStream(bytes, writable: false);
        var snapshot = SteamAccountSnapshotParser.Parse(stream);
        var recent = snapshot.Accounts
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.Timestamp)
            .ToList();
        var alphabetical = snapshot.Accounts
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var search = snapshot.Accounts
            .Where(a => a.DisplayName.Contains("User 00042", StringComparison.OrdinalIgnoreCase)
                || a.AccountName.Contains("user00042", StringComparison.OrdinalIgnoreCase))
            .ToList();

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        output.WriteLine($"{count:N0} contas: {stopwatch.ElapsedMilliseconds} ms, {allocated / 1_048_576d:F2} MB alocados");

        snapshot.Accounts.Should().HaveCount(count);
        snapshot.ActiveAccount.Should().NotBeNull();
        snapshot.ActiveAccount!.SteamId64.Should().Be(snapshot.Accounts[^1].SteamId64);
        recent[0].IsActive.Should().BeTrue();
        alphabetical[0].IsActive.Should().BeTrue();
        search.Should().ContainSingle();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        allocated.Should().BeLessThan(512L * 1024 * 1024);
    }

    [Theory]
    [MemberData(nameof(ScaleSizes))]
    public void IncrementalReconciliation_PreservesExistingCards(int count)
    {
        var original = CreateAccounts(count);
        var cards = original.Select(a => new AccountCardViewModel(a)).ToList();
        var existingById = cards.ToDictionary(c => c.Account.SteamId64);

        var incoming = CreateAccounts(count)
            .Skip(10)
            .Append(CreateAccount(count + 1))
            .ToList();
        var reconciled = incoming.Select(account =>
        {
            if (existingById.TryGetValue(account.SteamId64, out var existing))
            {
                existing.ApplySnapshot(account);
                return existing;
            }
            return new AccountCardViewModel(account);
        }).ToList();

        reconciled.Should().HaveCount(count - 9);
        reconciled.Take(count - 10).Should().OnlyContain(
            card => existingById[card.Account.SteamId64] == card);
        reconciled.Should().NotContain(card => card.Account.SteamId64 == original[0].SteamId64);
        reconciled.Should().Contain(card => card.Account.SteamId64 == CreateAccount(count + 1).SteamId64);
    }

    [Theory]
    [MemberData(nameof(ScaleSizes))]
    public void ReplaceAll_EmitsSingleCollectionReset(int count)
    {
        var collection = new RangeObservableCollection<SteamAccount>();
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => changes.Add(e.Action);

        collection.ReplaceAll(CreateAccounts(count));

        collection.Should().HaveCount(count);
        changes.Should().Equal(NotifyCollectionChangedAction.Reset);
    }

    [Theory]
    [MemberData(nameof(ScaleSizes))]
    public async Task BoundedQueue_ProcessesAllItemsWithFourWorkers(int count)
    {
        var activeWorkers = 0;
        var maximumWorkers = 0;
        var processed = 0;
        var items = Enumerable.Range(0, count).ToList();

        await BoundedWorkQueue.RunAsync(
            items,
            workerCount: 4,
            async (_, ct) =>
            {
                var active = Interlocked.Increment(ref activeWorkers);
                UpdateMaximum(ref maximumWorkers, active);
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                Interlocked.Increment(ref processed);
                Interlocked.Decrement(ref activeWorkers);
            });

        processed.Should().Be(count);
        maximumWorkers.Should().BeLessThanOrEqualTo(4);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (candidate <= observed) return;
            if (Interlocked.CompareExchange(ref maximum, candidate, observed) == observed) return;
        }
    }

    private static List<SteamAccount> CreateAccounts(int count)
        => Enumerable.Range(0, count).Select(CreateAccount).ToList();

    private static SteamAccount CreateAccount(int index) => new()
    {
        SteamId64 = (76561190000000000UL + (ulong)index).ToString(),
        AccountName = $"user{index:D5}",
        PersonaName = $"User {index:D5}",
        Timestamp = 1_700_000_000L + index,
        AutoLogin = index == 0,
        MostRecent = index == 0,
        IsActive = index == 0
    };

    private static string CreateLoginUsersVdf(int count)
    {
        var builder = new StringBuilder(count * 230);
        builder.AppendLine("\"users\"");
        builder.AppendLine("{");
        for (var i = 0; i < count; i++)
        {
            var id = 76561190000000000UL + (ulong)i;
            builder.AppendLine($"\t\"{id}\"");
            builder.AppendLine("\t{");
            builder.AppendLine($"\t\t\"AccountName\"\t\t\"user{i:D5}\"");
            builder.AppendLine($"\t\t\"PersonaName\"\t\t\"User {i:D5}\"");
            builder.AppendLine("\t\t\"RememberPassword\"\t\t\"1\"");
            builder.AppendLine($"\t\t\"MostRecent\"\t\t\"{(i == count - 1 ? 1 : 0)}\"");
            builder.AppendLine($"\t\t\"AutoLogin\"\t\t\"{(i == count - 1 ? 1 : 0)}\"");
            builder.AppendLine($"\t\t\"Timestamp\"\t\t\"{1_700_000_000L + i}\"");
            builder.AppendLine("\t}");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }
}
