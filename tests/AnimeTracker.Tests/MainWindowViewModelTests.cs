using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Jikan;
using AnimeTracker.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;

    public MainWindowViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_mainvm_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _repository = new MediaItemRepository(_connectionString);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try
            {
                SqliteConnection.ClearAllPools();
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Fact]
    public void Constructor_InitializesWithCurrentlyActiveSection()
    {
        var vm = new MainWindowViewModel(_repository);

        Assert.Equal(NavigationSection.CurrentlyActive, vm.SelectedSection);
        Assert.Equal("Currently Active (Watching / Reading)", vm.CurrentSectionTitle);
        Assert.Equal(0, vm.ActiveCount);
        Assert.Equal(0, vm.TotalLibraryCount);
    }

    [Theory]
    [InlineData(NavigationSection.AllLibrary, "All Library")]
    [InlineData(NavigationSection.PlanToWatchRead, "Plan to Watch / Read")]
    [InlineData(NavigationSection.Completed, "Completed")]
    [InlineData(NavigationSection.OnHoldDropped, "On Hold / Dropped")]
    [InlineData(NavigationSection.Recommendations, "Personalized Recommendations")]
    [InlineData(NavigationSection.SettingsAndBackup, "Settings & Backup")]
    public void SelectSectionCommand_UpdatesSelectedSectionAndTitle(NavigationSection section, string expectedTitle)
    {
        var vm = new MainWindowViewModel(_repository);

        vm.SelectSectionCommand.Execute(section);

        Assert.Equal(section, vm.SelectedSection);
        Assert.Equal(expectedTitle, vm.CurrentSectionTitle);
    }

    [Fact]
    public async Task OpenAddTitleCommand_TriggersOpenAddTitleRequestedEvent()
    {
        var vm = new MainWindowViewModel(_repository);
        var eventTriggered = false;
        vm.OpenAddTitleRequested += () =>
        {
            eventTriggered = true;
            return Task.CompletedTask;
        };

        await vm.OpenAddTitleCommand.ExecuteAsync(null);

        Assert.True(eventTriggered);
    }

    [Fact]
    public async Task RefreshCountsAsync_CalculatesCorrectCountsForEachCategory()
    {
        await _repository.AddAsync(new MediaItem { Title = "Anime 1", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Manga 1", Type = MediaType.Manga, Status = MediaStatus.Reading });
        await _repository.AddAsync(new MediaItem { Title = "Anime 2", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch });
        await _repository.AddAsync(new MediaItem { Title = "Anime 3", Type = MediaType.Anime, Status = MediaStatus.Completed });
        await _repository.AddAsync(new MediaItem { Title = "Manga 2", Type = MediaType.Manga, Status = MediaStatus.OnHold });

        var vm = new MainWindowViewModel(_repository);
        await vm.RefreshCountsAsync();

        Assert.Equal(5, vm.TotalLibraryCount);
        Assert.Equal(2, vm.ActiveCount); // Watching (1) + Reading (1)
        Assert.Equal(1, vm.PlanToCount);
        Assert.Equal(1, vm.CompletedCount);
        Assert.Equal(1, vm.OnHoldDroppedCount);
    }

    [Fact]
    public async Task LoadRecommendationsAsync_LoadsCandidates_WhenFavoritesExist()
    {
        await _repository.AddAsync(new MediaItem
        {
            Title = "Hunter x Hunter",
            Type = MediaType.Anime,
            Status = MediaStatus.Completed,
            Score = 10,
            Genres = "Action, Adventure"
        });

        var json = """
        {
            "data": [
                {
                    "mal_id": 200,
                    "title": "Jujutsu Kaisen",
                    "score": 8.7,
                    "genres": [ { "name": "Action" }, { "name": "Fantasy" } ]
                }
            ]
        }
        """;

        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var recService = new RecommendationService(jikanClient);
        var vm = new MainWindowViewModel(_repository, jikanClient, recommendationService: recService);

        await vm.LoadLibraryAsync();
        vm.SelectSectionCommand.Execute(NavigationSection.Recommendations);
        await vm.LoadRecommendationsAsync();

        Assert.True(vm.HasRecommendations);
        Assert.Single(vm.Recommendations);
        Assert.Equal("Jujutsu Kaisen", vm.Recommendations[0].Title);
        Assert.False(vm.IsLoadingRecommendations);
        Assert.False(vm.HasRecommendationError);
    }

    [Fact]
    public async Task LoadRecommendationsAsync_HandlesError_Gracefully()
    {
        await _repository.AddAsync(new MediaItem
        {
            Title = "Top Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.Completed,
            Score = 9,
            Genres = "Action"
        });

        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var recService = new RecommendationService(jikanClient);
        var vm = new MainWindowViewModel(_repository, jikanClient, recommendationService: recService);

        await vm.LoadLibraryAsync();
        vm.SelectSectionCommand.Execute(NavigationSection.Recommendations);
        await vm.LoadRecommendationsAsync();

        Assert.False(vm.HasRecommendations);
        Assert.True(vm.HasRecommendationError);
        Assert.NotNull(vm.RecommendationErrorMessage);
    }

    [Fact]
    public async Task AddRecommendationToLibraryAsync_AddsTitle_AndRemovesCardFromRecommendations()
    {
        var vm = new MainWindowViewModel(_repository);
        var mediaItem = new MediaItem
        {
            Id = 0,
            MalId = 777,
            MalType = "anime",
            Title = "Recommended Show",
            Type = MediaType.Anime,
            Status = MediaStatus.PlanToWatch,
            Genres = "Action"
        };
        var recItem = new RecommendationItem(mediaItem, 50, new[] { "Action" }, "Matches Action");
        var card = new RecommendationCardViewModel(recItem);
        vm.Recommendations.Add(card);
        vm.HasRecommendations = true;

        await vm.AddRecommendationToLibraryAsync(card);

        // 1. Should be added to DB
        var inDb = await _repository.GetByMalIdAsync(777, "anime");
        Assert.NotNull(inDb);
        Assert.Equal("Recommended Show", inDb.Title);
        Assert.Equal(MediaStatus.PlanToWatch, inDb.Status);

        // 2. Card should be removed from Recommendations
        Assert.DoesNotContain(card, vm.Recommendations);
        Assert.False(vm.HasRecommendations);
        Assert.Equal(1, vm.TotalLibraryCount);
        Assert.Equal(1, vm.PlanToCount);
    }

    [Fact]
    public async Task AddRecommendationToLibraryAsync_GuardsAgainstDuplicate_InLibrary()
    {
        await _repository.AddAsync(new MediaItem
        {
            MalId = 888,
            MalType = "anime",
            Title = "Already Present",
            Type = MediaType.Anime,
            Status = MediaStatus.Completed
        });

        var vm = new MainWindowViewModel(_repository);
        await vm.LoadLibraryAsync();

        var recItem = new RecommendationItem(new MediaItem
        {
            Id = 0,
            MalId = 888,
            MalType = "anime",
            Title = "Already Present (Recommendation)",
            Type = MediaType.Anime
        }, 50, new[] { "Action" }, "Matches Action");

        var card = new RecommendationCardViewModel(recItem);
        vm.Recommendations.Add(card);

        await vm.AddRecommendationToLibraryAsync(card);

        // Card should be removed and not duplicated in DB
        Assert.DoesNotContain(card, vm.Recommendations);
        var all = await _repository.GetAllAsync();
        Assert.Single(all); // still only 1 item
    }

    [Fact]
    public void SelectDiscoveryTabCommand_SwitchesTab()
    {
        var vm = new MainWindowViewModel(_repository);
        Assert.Equal(DiscoveryTab.Recommendations, vm.SelectedDiscoveryTab);
        Assert.True(vm.IsDiscoveryRecommendationsTab);
        Assert.False(vm.IsDiscoverySearchTab);

        vm.SelectDiscoveryTabCommand.Execute(DiscoveryTab.Search);
        Assert.Equal(DiscoveryTab.Search, vm.SelectedDiscoveryTab);
        Assert.False(vm.IsDiscoveryRecommendationsTab);
        Assert.True(vm.IsDiscoverySearchTab);
    }

    [Fact]
    public async Task SelectDiscoveryPillCommand_SwitchesPillAndFilters()
    {
        var vm = new MainWindowViewModel(_repository);
        Assert.Equal(DiscoveryPill.ForYou, vm.SelectedDiscoveryPill);
        Assert.True(vm.IsForYouPillActive);
        Assert.False(vm.IsPopularPillActive);

        await vm.SelectDiscoveryPillCommand.ExecuteAsync(DiscoveryPill.Popular);
        Assert.Equal(DiscoveryPill.Popular, vm.SelectedDiscoveryPill);
        Assert.True(vm.IsPopularPillActive);
        Assert.False(vm.IsForYouPillActive);

        await vm.SelectDiscoveryPillCommand.ExecuteAsync(DiscoveryPill.Anime);
        Assert.Equal(DiscoveryPill.Anime, vm.SelectedDiscoveryPill);
        Assert.Equal(MediaTypeFilter.Anime, vm.SelectedMediaTypeFilter);
        Assert.True(vm.IsAnimePillActive);

        await vm.SelectDiscoveryPillCommand.ExecuteAsync(DiscoveryPill.Manga);
        Assert.Equal(DiscoveryPill.Manga, vm.SelectedDiscoveryPill);
        Assert.Equal(MediaTypeFilter.Manga, vm.SelectedMediaTypeFilter);
        Assert.True(vm.IsMangaPillActive);

        await vm.SelectDiscoveryPillCommand.ExecuteAsync(DiscoveryPill.Manhwa);
        Assert.Equal(DiscoveryPill.Manhwa, vm.SelectedDiscoveryPill);
        Assert.Equal(MediaTypeFilter.Manhwa, vm.SelectedMediaTypeFilter);
        Assert.True(vm.IsManhwaPillActive);
    }

    [Fact]
    public void ClearCatalogSearchCommand_ClearsQuery_AndRestoresRecommendationsView()
    {
        var vm = new MainWindowViewModel(_repository);
        vm.CatalogSearchQuery = "Frieren";
        Assert.True(vm.IsShowingSearchResults);
        Assert.True(vm.HasCatalogSearchQuery);

        vm.ClearCatalogSearchCommand.Execute(null);
        Assert.Equal(string.Empty, vm.CatalogSearchQuery);
        Assert.False(vm.IsShowingSearchResults);
        Assert.False(vm.HasCatalogSearchQuery);
    }

    [Fact]
    public async Task DiscoverySectionTitleAndSubtitle_ReflectActiveState()
    {
        var vm = new MainWindowViewModel(_repository);
        Assert.Equal("Recommended for you", vm.DiscoverySectionTitle);
        Assert.Equal("Based on your highly rated titles", vm.DiscoverySectionSubtitle);

        await vm.SelectDiscoveryPillCommand.ExecuteAsync(DiscoveryPill.Popular);
        Assert.Equal("Popular titles", vm.DiscoverySectionTitle);
        Assert.Equal("Trending & community favorites", vm.DiscoverySectionSubtitle);

        vm.CatalogSearchQuery = "Solo Leveling";
        Assert.Equal("Search results for \"Solo Leveling\"", vm.DiscoverySectionTitle);
    }

    [Fact]
    public async Task DynamicDiscovery_TransitionsCorrectly_BetweenRecommendationsAndSearch()
    {
        var vm = new MainWindowViewModel(_repository);
        vm.SelectSection(NavigationSection.Recommendations);
        Assert.True(vm.IsRecommendationsSection);
        Assert.False(vm.IsShowingSearchResults);
        Assert.Equal("Recommended for you", vm.DiscoverySectionTitle);

        // When user enters a search query
        vm.CatalogSearchQuery = "Attack on Titan";
        Assert.True(vm.IsShowingSearchResults);
        Assert.Equal("Search results for \"Attack on Titan\"", vm.DiscoverySectionTitle);

        // When search is cleared
        vm.ClearCatalogSearchCommand.Execute(null);
        Assert.False(vm.IsShowingSearchResults);
        Assert.Equal("Recommended for you", vm.DiscoverySectionTitle);

        // When switching pill
        await vm.SelectDiscoveryPillCommand.ExecuteAsync(DiscoveryPill.Popular);
        Assert.Equal("Popular titles", vm.DiscoverySectionTitle);
        Assert.Equal("Trending & community favorites", vm.DiscoverySectionSubtitle);
    }

    [Fact]
    public async Task ShowRecommendationSuccess_SetsMessage_AndAutoDismissesAfterDelay()
    {
        var vm = new MainWindowViewModel(_repository);
        vm.ShowRecommendationSuccess("Test success message", durationMs: 50);

        Assert.Equal("Test success message", vm.RecommendationSuccessMessage);
        await Task.Delay(100);
        Assert.Null(vm.RecommendationSuccessMessage);
    }

    [Fact]
    public async Task ShowRecommendationSuccess_NewMessage_CancelsPreviousDismissalTimer()
    {
        var vm = new MainWindowViewModel(_repository);
        // First message with 80ms delay
        vm.ShowRecommendationSuccess("First message", durationMs: 80);

        // 40ms later, second message arrives with 150ms delay
        await Task.Delay(40);
        vm.ShowRecommendationSuccess("Second message", durationMs: 150);

        // At 100ms (when first message would have expired): second message is still visible!
        await Task.Delay(60);
        Assert.Equal("Second message", vm.RecommendationSuccessMessage);

        // At 220ms total: second message has expired
        await Task.Delay(120);
        Assert.Null(vm.RecommendationSuccessMessage);
    }

    [Fact]
    public async Task ShowRecommendationSuccess_DoesNotDismiss_RecommendationErrorMessage()
    {
        var vm = new MainWindowViewModel(_repository);
        vm.RecommendationErrorMessage = "Critical error occurred";
        vm.ShowRecommendationSuccess("Success info", durationMs: 40);

        await Task.Delay(70);
        Assert.Null(vm.RecommendationSuccessMessage);
        Assert.Equal("Critical error occurred", vm.RecommendationErrorMessage);
    }

    [Fact]
    public async Task PerformCatalogSearchAsync_PopulatesCatalogSearchResults_AndChecksLibraryPresence()
    {
        // Add existing item to repository with MAL ID 100
        await _repository.AddAsync(new MediaItem
        {
            MalId = 100,
            MalType = "anime",
            Title = "Existing Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching
        });

        var json = """
        {
            "data": [
                {
                    "mal_id": 100,
                    "title": "Existing Anime",
                    "type": "TV",
                    "episodes": 12,
                    "score": 8.5
                },
                {
                    "mal_id": 200,
                    "title": "Brand New Anime",
                    "type": "TV",
                    "episodes": 24,
                    "score": 9.0
                }
            ]
        }
        """;

        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var vm = new MainWindowViewModel(_repository, jikanClient);

        vm.CatalogSearchMediaType = MediaType.Anime;
        await vm.PerformCatalogSearchAsync("Anime");

        Assert.True(vm.HasCatalogSearchResults);
        Assert.Equal(2, vm.CatalogSearchResults.Count);

        var existingCard = vm.CatalogSearchResults.First(x => x.MalId == 100);
        Assert.True(existingCard.IsInLibrary);

        var newCard = vm.CatalogSearchResults.First(x => x.MalId == 200);
        Assert.False(newCard.IsInLibrary);
        Assert.Equal("Brand New Anime", newCard.Title);
        Assert.Equal(24, newCard.TotalProgress);
    }

    [Fact]
    public async Task PerformCatalogSearchAsync_HandlesEmptyResults_SetsEmptyState()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var vm = new MainWindowViewModel(_repository, jikanClient);

        await vm.PerformCatalogSearchAsync("NonExistentTitle");

        Assert.False(vm.HasCatalogSearchResults);
        Assert.True(vm.ShowEmptyCatalogSearchState);
        Assert.Contains("NonExistentTitle", vm.CatalogSearchEmptyMessage);
    }

    [Fact]
    public async Task PerformCatalogSearchAsync_HandlesRateLimit_SetsErrorMessage()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"status\":429,\"message\":\"Rate limit reached\"}")
            }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var vm = new MainWindowViewModel(_repository, jikanClient);

        await vm.PerformCatalogSearchAsync("Test");

        Assert.False(vm.HasCatalogSearchResults);
        Assert.True(vm.HasCatalogSearchError);
        Assert.Contains("rate limit", vm.CatalogSearchErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddCatalogSearchResultToLibraryAsync_AddsToDatabase_AndUpdatesCardIsInLibrary()
    {
        var vm = new MainWindowViewModel(_repository);
        var searchResult = new JikanSearchResult
        {
            MalId = 300,
            MalType = "anime",
            Title = "Solo Leveling",
            TotalCount = 12,
            Score = 8.5
        };
        var card = new CatalogSearchCardViewModel(searchResult, MediaType.Anime, isInLibrary: false);
        vm.CatalogSearchResults.Add(card);

        await vm.AddCatalogSearchResultToLibraryAsync(card);

        Assert.True(card.IsInLibrary);
        var inDb = await _repository.GetByMalIdAsync(300, "anime");
        Assert.NotNull(inDb);
        Assert.Equal("Solo Leveling", inDb.Title);
        Assert.Equal(MediaStatus.PlanToWatch, inDb.Status);
        Assert.Equal(1, vm.TotalLibraryCount);
        Assert.Equal(1, vm.PlanToCount);
        Assert.Contains("Solo Leveling", vm.RecommendationSuccessMessage);
    }

    [Fact]
    public async Task AddCatalogSearchResultToLibraryAsync_WhenAlreadyInLibrary_DoesNotDuplicate()
    {
        await _repository.AddAsync(new MediaItem
        {
            MalId = 400,
            MalType = "manga",
            Title = "Chainsaw Man",
            Type = MediaType.Manga,
            Status = MediaStatus.Reading
        });

        var vm = new MainWindowViewModel(_repository);
        await vm.LoadLibraryAsync();

        var card = new CatalogSearchCardViewModel(new JikanSearchResult
        {
            MalId = 400,
            MalType = "manga",
            Title = "Chainsaw Man"
        }, MediaType.Manga, isInLibrary: false);

        await vm.AddCatalogSearchResultToLibraryAsync(card);

        Assert.True(card.IsInLibrary);
        var all = await _repository.GetAllAsync();
        Assert.Single(all); // Not duplicated
        Assert.Contains("already in your library", vm.RecommendationSuccessMessage);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        }
    }
}
