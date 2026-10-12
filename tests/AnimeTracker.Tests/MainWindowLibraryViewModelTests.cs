using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Jikan;
using AnimeTracker.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class MainWindowLibraryViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;
    private readonly MediaTrackingService _trackingService;

    public MainWindowLibraryViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_libvm_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _repository = new MediaItemRepository(_connectionString);
        _trackingService = new MediaTrackingService(_repository);
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

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(_repository, new JikanApiClient(), _trackingService);
    }

    [Fact]
    public async Task LoadLibraryAsync_PopulatesFilteredItemsFromRepository()
    {
        await _repository.AddAsync(new MediaItem { Title = "Frieren", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Berserk", Type = MediaType.Manga, Status = MediaStatus.Reading });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task NavigationSection_CurrentlyActive_ShowsOnlyWatchingAndReading()
    {
        await _repository.AddAsync(new MediaItem { Title = "Active Anime", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Active Manga", Type = MediaType.Manga, Status = MediaStatus.Reading });
        await _repository.AddAsync(new MediaItem { Title = "Plan Anime", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch });
        await _repository.AddAsync(new MediaItem { Title = "Completed Manga", Type = MediaType.Manga, Status = MediaStatus.Completed });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectSection(NavigationSection.CurrentlyActive);

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, item => Assert.True(item.Status == MediaStatus.Watching || item.Status == MediaStatus.Reading));
    }

    [Fact]
    public async Task NavigationSection_PlanToWatchRead_ShowsOnlyPlanStatuses()
    {
        await _repository.AddAsync(new MediaItem { Title = "Active Anime", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Plan Anime", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch });
        await _repository.AddAsync(new MediaItem { Title = "Plan Manga", Type = MediaType.Manga, Status = MediaStatus.PlanToRead });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectSection(NavigationSection.PlanToWatchRead);

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, item => Assert.True(item.Status == MediaStatus.PlanToWatch || item.Status == MediaStatus.PlanToRead));
    }

    [Fact]
    public async Task NavigationSection_Completed_ShowsOnlyCompletedTitles()
    {
        await _repository.AddAsync(new MediaItem { Title = "Active", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Completed 1", Type = MediaType.Anime, Status = MediaStatus.Completed });
        await _repository.AddAsync(new MediaItem { Title = "Completed 2", Type = MediaType.Manga, Status = MediaStatus.Completed });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectSection(NavigationSection.Completed);

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, item => Assert.Equal(MediaStatus.Completed, item.Status));
    }

    [Fact]
    public async Task SearchText_FiltersCaseInsensitively()
    {
        await _repository.AddAsync(new MediaItem { Title = "Attack on Titan", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Solo Leveling", Type = MediaType.Manhwa, Status = MediaStatus.Reading });
        await _repository.AddAsync(new MediaItem { Title = "Titan's Bride", Type = MediaType.Manga, Status = MediaStatus.Reading });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SearchText = "titan";

        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, item => Assert.Contains("titan", item.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MediaTypeFilter_FiltersBySpecificMediaType()
    {
        await _repository.AddAsync(new MediaItem { Title = "Anime 1", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Manga 1", Type = MediaType.Manga, Status = MediaStatus.Reading });
        await _repository.AddAsync(new MediaItem { Title = "Manhwa 1", Type = MediaType.Manhwa, Status = MediaStatus.Reading });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectedMediaTypeFilter = MediaTypeFilter.Manhwa;

        Assert.Single(vm.FilteredItems);
        Assert.Equal("Manhwa 1", vm.FilteredItems[0].Title);
        Assert.Equal(MediaType.Manhwa, vm.FilteredItems[0].Type);
    }

    [Fact]
    public async Task CombinedFilters_ApplySectionSearchAndTypeSimultaneously()
    {
        await _repository.AddAsync(new MediaItem { Title = "Solo Leveling Anime", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Solo Leveling Manhwa", Type = MediaType.Manhwa, Status = MediaStatus.Reading });
        await _repository.AddAsync(new MediaItem { Title = "Solo Leveling Completed", Type = MediaType.Manhwa, Status = MediaStatus.Completed });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectSection(NavigationSection.CurrentlyActive);
        vm.SelectedMediaTypeFilter = MediaTypeFilter.Manhwa;
        vm.SearchText = "Leveling";

        Assert.Single(vm.FilteredItems);
        Assert.Equal("Solo Leveling Manhwa", vm.FilteredItems[0].Title);
    }

    [Fact]
    public async Task IsEmpty_IsTrueWhenNoItemsMatch()
    {
        await _repository.AddAsync(new MediaItem { Title = "Frieren", Type = MediaType.Anime, Status = MediaStatus.Watching });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SearchText = "NonexistentQuery";

        Assert.Empty(vm.FilteredItems);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task SortOrder_SortsByTitleAscending_AndScoreDescending()
    {
        await _repository.AddAsync(new MediaItem { Title = "Zeta", Type = MediaType.Anime, Status = MediaStatus.Watching, Score = 7 });
        await _repository.AddAsync(new MediaItem { Title = "Alpha", Type = MediaType.Anime, Status = MediaStatus.Watching, Score = 10 });
        await _repository.AddAsync(new MediaItem { Title = "Beta", Type = MediaType.Anime, Status = MediaStatus.Watching, Score = 9 });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectedSortOrder = MediaSortOrder.Title;
        Assert.Equal("Alpha", vm.FilteredItems[0].Title);
        Assert.Equal("Beta", vm.FilteredItems[1].Title);
        Assert.Equal("Zeta", vm.FilteredItems[2].Title);

        vm.SelectedSortOrder = MediaSortOrder.Score;
        Assert.Equal("Alpha", vm.FilteredItems[0].Title); // Score 10
        Assert.Equal("Beta", vm.FilteredItems[1].Title);  // Score 9
        Assert.Equal("Zeta", vm.FilteredItems[2].Title);  // Score 7
    }

    [Fact]
    public async Task CardIncrementAndDecrement_UpdatesProgress_AndPersistsToDatabase()
    {
        var item = new MediaItem
        {
            Title = "Progress Test",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 5,
            TotalProgress = 12
        };
        var id = await _repository.AddAsync(item);

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        var card = vm.FilteredItems.First(x => x.Id == id);
        Assert.Equal(5, card.CurrentProgress);

        await card.IncrementProgressCommand.ExecuteAsync(null);
        Assert.Equal(6, card.CurrentProgress);

        var dbItem = await _repository.GetByIdAsync(id);
        Assert.NotNull(dbItem);
        Assert.Equal(6, dbItem.CurrentProgress);

        await card.DecrementProgressCommand.ExecuteAsync(null);
        Assert.Equal(5, card.CurrentProgress);

        dbItem = await _repository.GetByIdAsync(id);
        Assert.NotNull(dbItem);
        Assert.Equal(5, dbItem.CurrentProgress);
    }

    [Fact]
    public async Task OpenDetailAsync_FetchesFreshItem_AndInvokesEvent()
    {
        var item = new MediaItem
        {
            Title = "Fresh Item",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching
        };
        var id = await _repository.AddAsync(item);

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        MediaItem? receivedItem = null;
        vm.OpenDetailRequested += freshItem =>
        {
            receivedItem = freshItem;
            return Task.CompletedTask;
        };

        var card = vm.FilteredItems.First(x => x.Id == id);
        await vm.OpenDetailAsync(card);

        Assert.NotNull(receivedItem);
        Assert.Equal(id, receivedItem.Id);
        Assert.Equal("Fresh Item", receivedItem.Title);
    }

    [Fact]
    public async Task RecommendationsSection_ComputesAndDisplaysRankedBacklog()
    {
        await _repository.AddAsync(new MediaItem
        {
            Title = "Fav Frieren",
            Type = MediaType.Anime,
            Status = MediaStatus.Completed,
            Score = 10,
            Genres = "Adventure, Fantasy"
        });

        await _repository.AddAsync(new MediaItem
        {
            Title = "Candidate Backlog",
            Type = MediaType.Anime,
            Status = MediaStatus.PlanToWatch,
            Genres = "Fantasy"
        });

        var vm = CreateViewModel();
        await vm.LoadLibraryAsync();

        vm.SelectSection(NavigationSection.Recommendations);

        Assert.True(vm.IsRecommendationsSection);
        Assert.False(vm.IsContentSection);
        Assert.True(vm.HasRecommendations);
        Assert.Single(vm.Recommendations);
        Assert.Equal("Candidate Backlog", vm.Recommendations[0].Title);
        Assert.Contains("Fantasy", vm.Recommendations[0].Reason);
    }
}
