using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Helpers;
using SteamSwitcher.ViewModels;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SteamSwitcher.Tests;

public sealed class GameScaleTests(ITestOutputHelper output)
{
    public static TheoryData<int> ScaleSizes => new() { 100, 500, 1_000, 5_000 };

    [Fact]
    public void UniqueKey_SeparatesSameGameAcrossInstallations()
    {
        var first = new SteamGame
        {
            AppId = "123",
            Name = "Same game",
            InstallationId = "primary",
        };
        var second = new SteamGame
        {
            AppId = "123",
            Name = "Same game",
            InstallationId = "secondary",
        };

        first.UniqueKey.Should().NotBe(second.UniqueKey);
    }

    [Theory]
    [MemberData(nameof(ScaleSizes))]
    public void FilterSortAndPaginate_RemainsBounded(int count)
    {
        var games = CreateCards(count);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var alphabetical = GameLibraryProjection.FilterAndSort(
            games, null, null, GameSortMode.Alphabetical);
        var mostPlayed = GameLibraryProjection.FilterAndSort(
            games, null, null, GameSortMode.MostPlayed);
        var largest = GameLibraryProjection.FilterAndSort(
            games, null, null, GameSortMode.LargestSize);
        var search = GameLibraryProjection.FilterAndSort(
            games, null, "Game 00042", GameSortMode.Alphabetical);
        var ownerGames = GameLibraryProjection.FilterAndSort(
            games, "owner-3", null, GameSortMode.Alphabetical);
        var lastPageNumber = (int)Math.Ceiling(count / 60d);
        var lastPage = GameLibraryProjection.GetPage(
            alphabetical, lastPageNumber, 60);

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        output.WriteLine(
            $"{count:N0} jogos: {stopwatch.ElapsedMilliseconds} ms, "
            + $"{allocated / 1_048_576d:F2} MB alocados");

        alphabetical.Should().HaveCount(count);
        alphabetical[0].Game.Name.Should().Be("Game 00000");
        mostPlayed[0].Game.PlaytimeMinutes.Should().Be(count - 1);
        largest[0].Game.SizeOnDisk.Should().Be(count * 1_048_576L);
        search.Should().ContainSingle();
        ownerGames.Should().OnlyContain(card =>
            card.Game.OwnerSteamId64 == "owner-3");
        lastPage.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(60);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        allocated.Should().BeLessThan(512L * 1024 * 1024);
    }

    [Theory]
    [MemberData(nameof(ScaleSizes))]
    public void Reconciliation_PreservesExistingCardsAndCoverImages(int count)
    {
        var existing = CreateCards(count);
        var byId = existing.ToDictionary(card => card.Game.AppId);
        var preservedCover = new System.Windows.Media.Imaging.BitmapImage();
        existing[20 % count].CoverImage = preservedCover;

        var incoming = Enumerable.Range(10, count - 10)
            .Select(CreateGame)
            .Append(CreateGame(count + 1))
            .ToList();
        var stopwatch = Stopwatch.StartNew();

        var reconciled = incoming.Select(game =>
        {
            if (!byId.TryGetValue(game.AppId, out var card))
                return new GameCardViewModel(game);

            card.ApplySnapshot(game);
            return card;
        }).ToList();

        stopwatch.Stop();
        output.WriteLine(
            $"Reconciliação de {count:N0} jogos: {stopwatch.ElapsedMilliseconds} ms");

        reconciled.Should().HaveCount(count - 9);
        reconciled.Take(count - 10).Should().OnlyContain(card =>
            ReferenceEquals(card, byId[card.Game.AppId]));
        reconciled.Should().NotContain(card => card.Game.AppId == "0");
        reconciled.Should().Contain(card => card.Game.AppId == (count + 1).ToString());

        if (count > 20)
            byId["20"].CoverImage.Should().BeSameAs(preservedCover);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    private static List<GameCardViewModel> CreateCards(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new GameCardViewModel(CreateGame(index)))
            .ToList();

    private static SteamGame CreateGame(int index) => new()
    {
        AppId = index.ToString(),
        Name = $"Game {index:D5}",
        InstallDir = $"game-{index}",
        LibraryPath = index % 2 == 0 ? @"C:\Steam" : @"D:\SteamLibrary",
        SizeOnDisk = (index + 1L) * 1_048_576L,
        PlaytimeMinutes = index,
        OwnerSteamId64 = $"owner-{index % 10}"
    };
}
