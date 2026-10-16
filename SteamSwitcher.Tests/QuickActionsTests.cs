using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Services;
using System.Diagnostics;
using ActivityKind = SteamSwitcher.Core.Services.ActivityKind;
using Xunit;
using Xunit.Abstractions;

namespace SteamSwitcher.Tests;

public sealed class QuickActionsTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("configuracoes", "Configurações")]
    [InlineData("FLAVIO", "Flávio")]
    public void Search_IgnoresAccentsAndCase(string query, string title)
    {
        QuickSearch.Find([new("id", "Conta", title, "", "")], query, []).Should().ContainSingle();
    }

    [Fact]
    public void Search_HandlesKeywordsEmptyAndNoResults()
    {
        QuickSearchEntry[] entries = [new("a", "Jogo", "Sekiro", "", "814380"), new("b", "Página", "Temas", "", "aparência")];
        QuickSearch.Find(entries, "814380", []).Single().Id.Should().Be("a");
        QuickSearch.Find(entries, "não existe", []).Should().BeEmpty();
        QuickSearch.Find(entries, "", ["b"]).First().Id.Should().Be("b");
    }

    [Fact]
    public void Search_1000Entries_ReportsTimingAndBoundsResults()
    {
        var entries = Enumerable.Range(0, 1000).Select(i => new QuickSearchEntry(i.ToString(), "Jogo", $"Jogo {i}", "", "instalado")).ToArray();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++) QuickSearch.Find(entries, "jogo", []).Should().HaveCount(40);
        output.WriteLine($"1000 entries, 50 searches: {timer.Elapsed.TotalMilliseconds:F1} ms total; {timer.Elapsed.TotalMilliseconds / 50:F2} ms/search.");
    }

    [Theory]
    [InlineData(true, false, false, 3, 2000)]
    [InlineData(false, true, false, 3, 2000)]
    [InlineData(false, false, true, 3, 2000)]
    [InlineData(false, false, false, 2, 2000)]
    [InlineData(false, false, false, 3, 5000)]
    public void Polling_OnlySlowsWhenHiddenAndIdle(bool visible, bool running, bool pending, int cycles, int expected)
        => RuntimePollingPolicy.DelayMilliseconds(visible, running, pending, cycles).Should().Be(expected);

    [Fact]
    public void Activity_IsBoundedNewestFirstAndClearable()
    {
        var journal = new ActivityJournal();
        for (var i = 0; i < 220; i++) journal.Record(ActivityKind.AccountSwitched);
        journal.Record(ActivityKind.GameLaunchFailed);
        journal.Snapshot().Should().HaveCount(200);
        journal.Snapshot().First().NeedsAttention.Should().BeTrue();
        journal.Clear();
        journal.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Activity_IdentifiesSourceAndTarget_AndDoesNotClaimFailedSwitchSucceeded()
    {
        var journal = new ActivityJournal();
        journal.Record(ActivityKind.AccountSwitched, "Flávio (@flavio)", "Teste (@teste)");
        journal.Snapshot().Single().Detail.Should().Contain("De: Flávio (@flavio)").And.Contain("Para: Teste (@teste)");
        journal.Record(ActivityKind.AccountSwitchFailed, null, "Destino");
        journal.Snapshot().First().Detail.Should().Contain("anterior não identificada").And.Contain("Troca não concluída");
    }

    [Fact]
    public void Metrics_ReportsFailuresAndRecentSampleWindow()
    {
        var metrics = new RuntimeMonitorMetrics();
        for (var i = 0; i < 301; i++) metrics.Record(10, i != 0);
        metrics.SetDelay(5000);
        metrics.Summary.Should().Contain("301 leituras").And.Contain("1 falhas").And.Contain("Últimas 300").And.Contain("5 s");
    }

    [Fact]
    public void RepairHistory_IdentifiesOperationInstallationAndAccounts_AndHandlesLegacyRecords()
    {
        var journal = new ActivityJournal();
        journal.Record(ActivityKind.PreferenceRepaired, operation: "Lembrar senha", installation: "C: Principal", accounts: ["Conta A", "Conta B"]);
        var entry = journal.Snapshot().Single();
        entry.Title.Should().Contain("Lembrar senha");
        entry.Detail.Should().Contain("C: Principal").And.Contain("Conta A").And.Contain("Conta B");
        new ActivityEntry(DateTimeOffset.UtcNow, ActivityKind.RepairFailed).Detail.Should().Contain("não registrada");
    }

    [Fact]
    public void Activity_PersistsAcrossInstances_AndClearPersists()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamNexusActivityTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "activity.json");
        try
        {
            var first = new ActivityJournal(path);
            first.Record(ActivityKind.AccountSwitched, "Origem", "Destino");
            var reopened = new ActivityJournal(path);
            reopened.Snapshot().Single().Detail.Should().Contain("Origem").And.Contain("Destino");
            reopened.StorageError.Should().BeNull();
            reopened.Clear();
            new ActivityJournal(path).Snapshot().Should().BeEmpty();
            File.WriteAllText(path, "invalid json");
            var corrupt = new ActivityJournal(path);
            corrupt.Snapshot().Should().BeEmpty();
            corrupt.StorageError.Should().NotBeNull();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
