using System;
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

public class AddTitleViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public int RequestCount { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            LastRequest = request;

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

    static AddTitleViewModelTests()
    {
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
        }
        catch
        {
            // Already initialized
        }
    }

    public AddTitleViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_addtitle_test_{Guid.NewGuid():N}.db");
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
                // Best effort cleanup
            }
        }
    }

    private JikanApiClient CreateTestJikanClient(string jsonResponse, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(jsonResponse)
            }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        return new JikanApiClient(httpClient);
    }

    [Fact]
    public async Task PerformSearchAsync_ForAnime_CallsSearchAnime_AndSetsResults()
    {
        var json = """
        {
            "data": [
                { "mal_id": 1, "title": "Cowboy Bebop", "episodes": 26, "score": 8.75, "year": 1998 }
            ]
        }
        """;
        var client = CreateTestJikanClient(json);
        var vm = new AddTitleViewModel(_repository, client);
        vm.SearchMediaType = MediaType.Anime;

        await vm.PerformSearchAsync("Bebop");

        Assert.Single(vm.SearchResults);
        Assert.Equal("Cowboy Bebop", vm.SearchResults[0].Title);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.SearchErrorMessage);
    }

    [Fact]
    public async Task PerformSearchAsync_ForManhwa_UsesMangaEndpoint()
    {
        var json = """
        {
            "data": [
                { "mal_id": 2, "title": "Tower of God", "chapters": 500, "score": 8.5 }
            ]
        }
        """;
        var client = CreateTestJikanClient(json);
        var vm = new AddTitleViewModel(_repository, client);
        vm.SearchMediaType = MediaType.Manhwa;

        await vm.PerformSearchAsync("Tower");

        Assert.Single(vm.SearchResults);
        Assert.Equal(2, vm.SearchResults[0].MalId);
        Assert.Equal("manga", vm.SearchResults[0].MalType);
    }

    [Fact]
    public async Task PerformSearchAsync_EmptyQuery_ClearsResults_AndDoesNotLoad()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);

        await vm.PerformSearchAsync("   ");

        Assert.Empty(vm.SearchResults);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task PerformSearchAsync_WhenRateLimited_SetsSearchErrorMessage()
    {
        var client = CreateTestJikanClient("", HttpStatusCode.TooManyRequests);
        var vm = new AddTitleViewModel(_repository, client);

        await vm.PerformSearchAsync("Naruto");

        Assert.Empty(vm.SearchResults);
        Assert.NotNull(vm.SearchErrorMessage);
        Assert.Contains("rate limit", vm.SearchErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddSelectedCatalogTitleAsync_WhenDuplicate_SetsWarning_AndDoesNotInsert()
    {
        // Existing item with MalId 100, malType anime
        await _repository.AddAsync(new MediaItem
        {
            MalId = 100,
            MalType = "anime",
            Title = "Existing Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching
        });

        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.SelectedSearchResult = new JikanSearchResult
        {
            MalId = 100,
            MalType = "anime",
            Title = "Existing Anime Duplicate",
            TotalCount = 12
        };

        var added = await vm.AddSelectedCatalogTitleAsync();

        Assert.False(added);
        Assert.NotNull(vm.ActionMessage);
        Assert.Contains("already in your library", vm.ActionMessage, StringComparison.OrdinalIgnoreCase);

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task AddSelectedCatalogTitleAsync_WhenValidAnime_AddsWithPlanToWatch_AndZeroProgress()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.SearchMediaType = MediaType.Anime;
        vm.SelectedSearchResult = new JikanSearchResult
        {
            MalId = 200,
            MalType = "anime",
            Title = "New Anime",
            TotalCount = 24,
            CoverImageUrl = "https://example.com/cover.jpg",
            Genres = new[] { "Action", "Sci-Fi" },
            Synopsis = "Cool anime story",
            Year = 2024
        };

        var added = await vm.AddSelectedCatalogTitleAsync();

        Assert.True(added);
        Assert.True(vm.WasAddedSuccessfully);

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        var saved = all[0];
        Assert.Equal(200, saved.MalId);
        Assert.Equal("anime", saved.MalType);
        Assert.Equal("New Anime", saved.Title);
        Assert.Equal(MediaType.Anime, saved.Type);
        Assert.Equal(MediaStatus.PlanToWatch, saved.Status);
        Assert.Equal(0, saved.CurrentProgress);
        Assert.Equal(24, saved.TotalProgress);
        Assert.Equal("Action, Sci-Fi", saved.Genres);
    }

    [Fact]
    public async Task AddSelectedCatalogTitleAsync_WhenValidManhwa_SetsMediaTypeManhwa_AndPlanToRead()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.SearchMediaType = MediaType.Manhwa;
        vm.SelectedSearchResult = new JikanSearchResult
        {
            MalId = 300,
            MalType = "manga",
            Title = "Solo Manhwa",
            TotalCount = 150
        };

        var added = await vm.AddSelectedCatalogTitleAsync();

        Assert.True(added);

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        var saved = all[0];
        Assert.Equal(MediaType.Manhwa, saved.Type);
        Assert.Equal("manga", saved.MalType);
        Assert.Equal(MediaStatus.PlanToRead, saved.Status);
        Assert.Equal(0, saved.CurrentProgress);
    }

    [Fact]
    public async Task AddManualTitleAsync_WhenTitleIsEmpty_SetsError_AndDoesNotAdd()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "   ";

        var added = await vm.AddManualTitleAsync();

        Assert.False(added);
        Assert.NotNull(vm.ManualErrorMessage);
        Assert.Contains("required", vm.ManualErrorMessage, StringComparison.OrdinalIgnoreCase);

        var all = await _repository.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task AddManualTitleAsync_WhenTotalProgressNegative_SetsError()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Custom Novel";
        vm.ManualTotalProgress = -5;

        var added = await vm.AddManualTitleAsync();

        Assert.False(added);
        Assert.NotNull(vm.ManualErrorMessage);

        var all = await _repository.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task AddManualTitleAsync_WhenValid_AddsWithNullCatalogIds_AndCorrectStatus()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Indie Webcomic";
        vm.ManualMediaType = MediaType.Manga;
        vm.ManualTotalProgress = 50;
        vm.ManualSynopsis = "Custom synopsis";

        var added = await vm.AddManualTitleAsync();

        Assert.True(added);
        Assert.True(vm.WasAddedSuccessfully);

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        var saved = all[0];
        Assert.Equal("Indie Webcomic", saved.Title);
        Assert.Null(saved.MalId);
        Assert.Null(saved.MalType);
        Assert.Equal(MediaType.Manga, saved.Type);
        Assert.Equal(MediaStatus.PlanToRead, saved.Status);
        Assert.Equal(0, saved.CurrentProgress);
        Assert.Equal(50, saved.TotalProgress);
    }

    [Fact]
    public async Task AddManualTitleAsync_WhenTitleMatchesExisting_WarnsFirst_AndAddsOnConfirm()
    {
        await _repository.AddAsync(new MediaItem
        {
            Title = "Duplicate Name Comic",
            Type = MediaType.Manga,
            Status = MediaStatus.Reading
        });

        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Duplicate Name Comic";
        vm.ManualMediaType = MediaType.Manga;

        // First attempt: warns user and returns false
        var firstAttempt = await vm.AddManualTitleAsync();
        Assert.False(firstAttempt);
        Assert.NotNull(vm.ManualWarningMessage);
        Assert.Contains("already exists", vm.ManualWarningMessage, StringComparison.OrdinalIgnoreCase);

        // Second attempt: user confirms addition
        var secondAttempt = await vm.AddManualTitleAsync();
        Assert.True(secondAttempt);

        var all = await _repository.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task AddManualTitleAsync_WithPositiveStartingProgress_SetsWatchingForAnime()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Custom Anime";
        vm.ManualMediaType = MediaType.Anime;
        vm.ManualCurrentProgress = 5;
        vm.ManualTotalProgress = 12;

        var added = await vm.AddManualTitleAsync();

        Assert.True(added);
        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(MediaStatus.Watching, all[0].Status);
        Assert.Equal(5, all[0].CurrentProgress);
    }

    [Fact]
    public async Task AddManualTitleAsync_WithPositiveStartingProgress_SetsReadingForManga()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Custom Manga";
        vm.ManualMediaType = MediaType.Manga;
        vm.ManualCurrentProgress = 25;
        vm.ManualTotalProgress = 100;

        var added = await vm.AddManualTitleAsync();

        Assert.True(added);
        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(MediaStatus.Reading, all[0].Status);
        Assert.Equal(25, all[0].CurrentProgress);
    }

    [Fact]
    public async Task AddManualTitleAsync_WithNegativeStartingProgress_SetsError()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Negative Progress Title";
        vm.ManualCurrentProgress = -2;

        var added = await vm.AddManualTitleAsync();

        Assert.False(added);
        Assert.NotNull(vm.ManualErrorMessage);
        Assert.Contains("negative", vm.ManualErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddManualTitleAsync_WithStartingProgressExceedingTotal_SetsError()
    {
        var client = CreateTestJikanClient("{\"data\":[]}");
        var vm = new AddTitleViewModel(_repository, client);
        vm.ManualTitle = "Exceeding Progress Title";
        vm.ManualCurrentProgress = 15;
        vm.ManualTotalProgress = 12;

        var added = await vm.AddManualTitleAsync();

        Assert.False(added);
        Assert.NotNull(vm.ManualErrorMessage);
        Assert.Contains("exceed", vm.ManualErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddManualTitleAsync_WithValidImage_SavesCoverAndSetsCoverImagePath()
    {
        var coversDir = Path.Combine(Path.GetTempPath(), $"animetracker_covers_{Guid.NewGuid():N}");
        Directory.CreateDirectory(coversDir);
        var storage = new CoverStorageService(coversDir);

        var tempImageFile = Path.Combine(Path.GetTempPath(), $"temp_cover_{Guid.NewGuid():N}.png");
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        await File.WriteAllBytesAsync(tempImageFile, validPng);

        try
        {
            var client = CreateTestJikanClient("{\"data\":[]}");
            var vm = new AddTitleViewModel(_repository, client, storage);
            vm.ManualTitle = "Custom Cover Title";
            vm.SetSelectedCoverFile(tempImageFile);

            Assert.NotNull(vm.ManualCoverPreview);
            Assert.True(vm.HasManualCover);

            var added = await vm.AddManualTitleAsync();
            Assert.True(added);

            var all = await _repository.GetAllAsync();
            Assert.Single(all);
            Assert.NotNull(all[0].CoverImagePath);
            Assert.StartsWith("covers/", all[0].CoverImagePath!);

            var fullPath = storage.GetFullPath(all[0].CoverImagePath!);
            Assert.True(File.Exists(fullPath));
        }
        finally
        {
            if (File.Exists(tempImageFile)) File.Delete(tempImageFile);
            if (Directory.Exists(coversDir)) Directory.Delete(coversDir, true);
        }
    }

    [Fact]
    public void RemoveImage_ClearsSelectedImageAndPreview()
    {
        var tempImageFile = Path.Combine(Path.GetTempPath(), $"temp_cover_{Guid.NewGuid():N}.png");
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        File.WriteAllBytes(tempImageFile, validPng);

        try
        {
            var client = CreateTestJikanClient("{\"data\":[]}");
            var vm = new AddTitleViewModel(_repository, client);
            vm.SetSelectedCoverFile(tempImageFile);

            Assert.True(vm.HasManualCover);
            Assert.NotNull(vm.ManualCoverPreview);

            vm.RemoveImage();

            Assert.False(vm.HasManualCover);
            Assert.Null(vm.ManualCoverPreview);
            Assert.Null(vm.ManualCoverFilePath);
        }
        finally
        {
            if (File.Exists(tempImageFile)) File.Delete(tempImageFile);
        }
    }
}
