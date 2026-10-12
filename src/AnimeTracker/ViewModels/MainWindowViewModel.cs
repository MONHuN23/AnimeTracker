using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Jikan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnimeTracker.ViewModels;

public enum NavigationSection
{
    CurrentlyActive,
    AllLibrary,
    PlanToWatchRead,
    Completed,
    OnHoldDropped,
    Recommendations,
    SettingsAndBackup
}

public enum MediaTypeFilter
{
    All,
    Anime,
    Manga,
    Manhwa
}

public enum MediaSortOrder
{
    LastUpdated,
    Title,
    Score,
    Progress
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly List<MediaItem> _allItems = new();

    public MediaItemRepository Repository { get; }
    public JikanApiClient JikanClient { get; }
    public MediaTrackingService TrackingService { get; }
    public RecommendationService RecommendationService { get; }

    public event Func<Task>? OpenAddTitleRequested;
    public event Func<MediaItem, Task>? OpenDetailRequested;

    [ObservableProperty]
    private string _title = "AnimeTracker";

    [ObservableProperty]
    private NavigationSection _selectedSection = NavigationSection.CurrentlyActive;

    [ObservableProperty]
    private string _currentSectionTitle = "Currently Active (Watching / Reading)";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private MediaTypeFilter _selectedMediaTypeFilter = MediaTypeFilter.All;

    [ObservableProperty]
    private MediaSortOrder _selectedSortOrder = MediaSortOrder.LastUpdated;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isContentSection = true;

    [ObservableProperty]
    private bool _isRecommendationsSection;

    [ObservableProperty]
    private bool _isSettingsAndBackupSection;

    [ObservableProperty]
    private bool _hasRecommendations;

    [ObservableProperty]
    private string? _recommendationEmptyMessage;

    [ObservableProperty]
    private string? _selectedRecommendationGenre = "All Genres";

    [ObservableProperty]
    private int _activeCount = 0;

    [ObservableProperty]
    private int _totalLibraryCount = 0;

    [ObservableProperty]
    private int _planToCount = 0;

    [ObservableProperty]
    private int _completedCount = 0;

    [ObservableProperty]
    private int _onHoldDroppedCount = 0;

    public ObservableCollection<MediaCardViewModel> FilteredItems { get; } = new();
    public ObservableCollection<RecommendationCardViewModel> Recommendations { get; } = new();
    public ObservableCollection<string> TopFavoriteGenres { get; } = new();

    public MainWindowViewModel(
        MediaItemRepository? repository = null,
        JikanApiClient? jikanClient = null,
        MediaTrackingService? trackingService = null,
        RecommendationService? recommendationService = null)
    {
        Repository = repository ?? new MediaItemRepository();
        JikanClient = jikanClient ?? new JikanApiClient();
        TrackingService = trackingService ?? new MediaTrackingService(Repository);
        RecommendationService = recommendationService ?? new RecommendationService();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedMediaTypeFilterChanged(MediaTypeFilter value)
    {
        ApplyFilter();
        if (IsRecommendationsSection)
        {
            LoadRecommendations();
        }
    }
    partial void OnSelectedSortOrderChanged(MediaSortOrder value) => ApplyFilter();
    partial void OnSelectedRecommendationGenreChanged(string? value) => LoadRecommendations();

    [RelayCommand]
    public void SelectSection(NavigationSection section)
    {
        SelectedSection = section;
        IsRecommendationsSection = section == NavigationSection.Recommendations;
        IsSettingsAndBackupSection = section == NavigationSection.SettingsAndBackup;
        CurrentSectionTitle = section switch
        {
            NavigationSection.CurrentlyActive => "Currently Active (Watching / Reading)",
            NavigationSection.AllLibrary => "All Library",
            NavigationSection.PlanToWatchRead => "Plan to Watch / Read",
            NavigationSection.Completed => "Completed",
            NavigationSection.OnHoldDropped => "On Hold / Dropped",
            NavigationSection.Recommendations => "Personalized Recommendations",
            NavigationSection.SettingsAndBackup => "Settings & Backup",
            _ => "Library"
        };

        ApplyFilter();
        if (IsRecommendationsSection)
        {
            LoadRecommendations();
        }
    }

    [RelayCommand]
    public async Task OpenAddTitleAsync()
    {
        if (OpenAddTitleRequested != null)
        {
            await OpenAddTitleRequested.Invoke();
        }
    }

    [RelayCommand]
    public async Task OpenDetailAsync(MediaCardViewModel? card)
    {
        if (card == null) return;
        var item = await Repository.GetByIdAsync(card.Id);
        if (item != null && OpenDetailRequested != null)
        {
            await OpenDetailRequested.Invoke(item);
        }
    }

    [RelayCommand]
    public async Task OpenRecommendationDetailAsync(RecommendationCardViewModel? card)
    {
        if (card == null) return;
        var item = await Repository.GetByIdAsync(card.Id);
        if (item != null && OpenDetailRequested != null)
        {
            await OpenDetailRequested.Invoke(item);
        }
    }

    [RelayCommand]
    public async Task LoadLibraryAsync()
    {
        var items = await Repository.GetAllAsync();
        _allItems.Clear();
        _allItems.AddRange(items);

        await RefreshCountsAsync();
        ApplyFilter();
        if (IsRecommendationsSection)
        {
            LoadRecommendations();
        }
    }

    public void LoadRecommendations()
    {
        if (SelectedSection != NavigationSection.Recommendations) return;

        var genreFilter = string.Equals(SelectedRecommendationGenre, "All Genres", StringComparison.OrdinalIgnoreCase)
            ? null
            : SelectedRecommendationGenre;

        var result = RecommendationService.GetRecommendations(
            _allItems,
            SelectedMediaTypeFilter,
            genreFilter);

        Recommendations.Clear();
        foreach (var rec in result.Recommendations)
        {
            var card = new RecommendationCardViewModel(rec);
            card.LoadCoverImage();
            Recommendations.Add(card);
        }

        var currentSelected = SelectedRecommendationGenre ?? "All Genres";
        TopFavoriteGenres.Clear();
        TopFavoriteGenres.Add("All Genres");
        foreach (var g in result.TopFavoriteGenres)
        {
            TopFavoriteGenres.Add(g);
        }

        if (TopFavoriteGenres.Contains(currentSelected))
        {
            SelectedRecommendationGenre = currentSelected;
        }
        else
        {
            SelectedRecommendationGenre = "All Genres";
        }

        HasRecommendations = result.HasRecommendations;
        RecommendationEmptyMessage = result.EmptyStateMessage;
    }

    [RelayCommand]
    public async Task RefreshCountsAsync()
    {
        var all = await Repository.GetAllAsync();
        TotalLibraryCount = all.Count;
        ActiveCount = all.Count(x => x.Status == MediaStatus.Watching || x.Status == MediaStatus.Reading);
        PlanToCount = all.Count(x => x.Status == MediaStatus.PlanToWatch || x.Status == MediaStatus.PlanToRead);
        CompletedCount = all.Count(x => x.Status == MediaStatus.Completed);
        OnHoldDroppedCount = all.Count(x => x.Status == MediaStatus.OnHold || x.Status == MediaStatus.Dropped);
    }

    public void ApplyFilter()
    {
        if (SelectedSection == NavigationSection.Recommendations ||
            SelectedSection == NavigationSection.SettingsAndBackup)
        {
            IsContentSection = false;
            FilteredItems.Clear();
            IsEmpty = false;
            return;
        }

        IsContentSection = true;

        IEnumerable<MediaItem> query = _allItems;

        // 1. Filter by Navigation Section
        query = SelectedSection switch
        {
            NavigationSection.CurrentlyActive => query.Where(x => x.Status == MediaStatus.Watching || x.Status == MediaStatus.Reading),
            NavigationSection.PlanToWatchRead => query.Where(x => x.Status == MediaStatus.PlanToWatch || x.Status == MediaStatus.PlanToRead),
            NavigationSection.Completed => query.Where(x => x.Status == MediaStatus.Completed),
            NavigationSection.OnHoldDropped => query.Where(x => x.Status == MediaStatus.OnHold || x.Status == MediaStatus.Dropped),
            _ => query // AllLibrary
        };

        // 2. Filter by Search Query
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            query = query.Where(x => x.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Filter by Media Type
        query = SelectedMediaTypeFilter switch
        {
            MediaTypeFilter.Anime => query.Where(x => x.Type == MediaType.Anime),
            MediaTypeFilter.Manga => query.Where(x => x.Type == MediaType.Manga),
            MediaTypeFilter.Manhwa => query.Where(x => x.Type == MediaType.Manhwa),
            _ => query
        };

        // 4. Sort
        query = SelectedSortOrder switch
        {
            MediaSortOrder.Title => query.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase),
            MediaSortOrder.Score => query.OrderByDescending(x => x.Score ?? -1).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase),
            MediaSortOrder.Progress => query.OrderByDescending(x => x.CurrentProgress).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(x => x.UpdatedAt) // LastUpdated
        };

        FilteredItems.Clear();
        foreach (var item in query)
        {
            var card = new MediaCardViewModel(item, TrackingService, OnCardUpdatedAsync);
            card.LoadCoverImage();
            FilteredItems.Add(card);
        }

        IsEmpty = FilteredItems.Count == 0;
    }

    private async Task OnCardUpdatedAsync(MediaCardViewModel card)
    {
        var match = _allItems.FirstOrDefault(x => x.Id == card.Id);
        if (match != null)
        {
            match.CurrentProgress = card.CurrentProgress;
            match.Status = card.Status;
            match.UpdatedAt = card.UpdatedAt;
        }

        await RefreshCountsAsync();

        // If the item status changed such that it should leave the current section view, re-filter
        if (SelectedSection == NavigationSection.CurrentlyActive &&
            (card.Status != MediaStatus.Watching && card.Status != MediaStatus.Reading))
        {
            ApplyFilter();
        }
    }
}
