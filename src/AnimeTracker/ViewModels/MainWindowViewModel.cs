using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Backup;
using AnimeTracker.Services.Jikan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

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

public enum DiscoveryTab
{
    Recommendations,
    Search
}

public enum DiscoveryPill
{
    ForYou,
    Popular,
    Anime,
    Manga,
    Manhwa
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly List<MediaItem> _allItems = new();

    public MediaItemRepository Repository { get; }
    public JikanApiClient JikanClient { get; }
    public MediaTrackingService TrackingService { get; }
    public RecommendationService RecommendationService { get; }
    public BackupService BackupService { get; }

    public event Func<Task>? OpenAddTitleRequested;
    public event Func<MediaItem, Task>? OpenDetailRequested;
    public event Func<Task>? RequestExportRequested;
    public event Func<Task>? RequestImportRequested;

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
    private bool _isLoadingRecommendations;

    [ObservableProperty]
    private string? _recommendationErrorMessage;

    [ObservableProperty]
    private bool _hasRecommendationError;

    [ObservableProperty]
    private bool _showEmptyRecommendationState;

    [ObservableProperty]
    private string? _recommendationSuccessMessage;

    [ObservableProperty]
    private string? _recommendationEmptyMessage;

    [ObservableProperty]
    private string? _selectedRecommendationGenre = "All Genres";

    private CancellationTokenSource? _recommendationsCts;
    private CancellationTokenSource? _catalogSearchCts;
    private CancellationTokenSource? _successMessageCts;
    private int _recommendationsRequestId = 0;
    private int _catalogSearchRequestId = 0;
    private bool _isChangingDiscoveryPill = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoveryRecommendationsTab))]
    [NotifyPropertyChangedFor(nameof(IsDiscoverySearchTab))]
    private DiscoveryTab _selectedDiscoveryTab = DiscoveryTab.Recommendations;

    public bool IsDiscoveryRecommendationsTab => SelectedDiscoveryTab == DiscoveryTab.Recommendations;
    public bool IsDiscoverySearchTab => SelectedDiscoveryTab == DiscoveryTab.Search;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForYouPillActive))]
    [NotifyPropertyChangedFor(nameof(IsPopularPillActive))]
    [NotifyPropertyChangedFor(nameof(IsAnimePillActive))]
    [NotifyPropertyChangedFor(nameof(IsMangaPillActive))]
    [NotifyPropertyChangedFor(nameof(IsManhwaPillActive))]
    [NotifyPropertyChangedFor(nameof(DiscoverySectionTitle))]
    [NotifyPropertyChangedFor(nameof(DiscoverySectionSubtitle))]
    private DiscoveryPill _selectedDiscoveryPill = DiscoveryPill.ForYou;

    public bool IsForYouPillActive => SelectedDiscoveryPill == DiscoveryPill.ForYou;
    public bool IsPopularPillActive => SelectedDiscoveryPill == DiscoveryPill.Popular;
    public bool IsAnimePillActive => SelectedDiscoveryPill == DiscoveryPill.Anime;
    public bool IsMangaPillActive => SelectedDiscoveryPill == DiscoveryPill.Manga;
    public bool IsManhwaPillActive => SelectedDiscoveryPill == DiscoveryPill.Manhwa;

    [ObservableProperty]
    private string _catalogSearchQuery = string.Empty;

    public bool IsShowingSearchResults => !string.IsNullOrWhiteSpace(CatalogSearchQuery);
    public bool HasCatalogSearchQuery => !string.IsNullOrWhiteSpace(CatalogSearchQuery);

    public string DiscoverySectionTitle
    {
        get
        {
            if (IsShowingSearchResults)
            {
                return $"Search results for \"{CatalogSearchQuery}\"";
            }
            return SelectedDiscoveryPill switch
            {
                DiscoveryPill.Popular => "Popular titles",
                DiscoveryPill.Anime => "Recommended anime",
                DiscoveryPill.Manga => "Recommended manga",
                DiscoveryPill.Manhwa => "Recommended manhwa",
                _ => "Recommended for you"
            };
        }
    }

    public string DiscoverySectionSubtitle
    {
        get
        {
            if (IsShowingSearchResults)
            {
                return IsLoadingCatalogSearch
                    ? "Searching catalog..."
                    : $"Found {CatalogSearchResults.Count} titles in catalog";
            }
            return SelectedDiscoveryPill switch
            {
                DiscoveryPill.Popular => "Trending & community favorites",
                DiscoveryPill.Anime => "Based on your anime taste & preferences",
                DiscoveryPill.Manga => "Based on your manga taste & preferences",
                DiscoveryPill.Manhwa => "Based on your manhwa taste & preferences",
                _ => "Based on your highly rated titles"
            };
        }
    }

    [ObservableProperty]
    private MediaType _catalogSearchMediaType = MediaType.Anime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscoverySectionSubtitle))]
    private bool _isLoadingCatalogSearch;

    [ObservableProperty]
    private bool _hasCatalogSearchError;

    [ObservableProperty]
    private string? _catalogSearchErrorMessage;

    [ObservableProperty]
    private bool _hasCatalogSearchResults;

    [ObservableProperty]
    private bool _showEmptyCatalogSearchState;

    [ObservableProperty]
    private string? _catalogSearchEmptyMessage;

    public ObservableCollection<CatalogSearchCardViewModel> CatalogSearchResults { get; } = new();

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

    [ObservableProperty]
    private string? _backupStatusMessage;

    [ObservableProperty]
    private string? _backupErrorMessage;

    [ObservableProperty]
    private bool _isImportConfirming;

    [ObservableProperty]
    private int _validatedImportCount;

    [ObservableProperty]
    private List<MediaItem>? _pendingImportItems;

    public ObservableCollection<MediaCardViewModel> FilteredItems { get; } = new();
    public ObservableCollection<RecommendationCardViewModel> Recommendations { get; } = new();
    public ObservableCollection<string> TopFavoriteGenres { get; } = new();

    public MainWindowViewModel(
        MediaItemRepository? repository = null,
        JikanApiClient? jikanClient = null,
        MediaTrackingService? trackingService = null,
        RecommendationService? recommendationService = null,
        BackupService? backupService = null)
    {
        Repository = repository ?? new MediaItemRepository();
        JikanClient = jikanClient ?? new JikanApiClient();
        TrackingService = trackingService ?? new MediaTrackingService(Repository);
        RecommendationService = recommendationService ?? new RecommendationService(JikanClient);
        BackupService = backupService ?? new BackupService(Repository.ConnectionString);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedMediaTypeFilterChanged(MediaTypeFilter value)
    {
        ApplyFilter();
        if (IsRecommendationsSection && !_isChangingDiscoveryPill)
        {
            _ = LoadRecommendationsAsync();
        }
    }
    partial void OnSelectedSortOrderChanged(MediaSortOrder value) => ApplyFilter();
    partial void OnSelectedRecommendationGenreChanged(string? value)
    {
        if (IsRecommendationsSection && !_isChangingDiscoveryPill)
        {
            _ = LoadRecommendationsAsync();
        }
    }

    partial void OnCatalogSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsShowingSearchResults));
        OnPropertyChanged(nameof(HasCatalogSearchQuery));
        OnPropertyChanged(nameof(DiscoverySectionTitle));
        OnPropertyChanged(nameof(DiscoverySectionSubtitle));

        _catalogSearchCts?.Cancel();
        _catalogSearchCts?.Dispose();
        _catalogSearchCts = new CancellationTokenSource();
        _ = TriggerCatalogSearchDebounceAsync(value, _catalogSearchCts.Token);
    }

    partial void OnCatalogSearchMediaTypeChanged(MediaType value)
    {
        if (!string.IsNullOrWhiteSpace(CatalogSearchQuery))
        {
            _catalogSearchCts?.Cancel();
            _catalogSearchCts?.Dispose();
            _catalogSearchCts = new CancellationTokenSource();
            _ = PerformCatalogSearchAsync(CatalogSearchQuery, _catalogSearchCts.Token);
        }
    }

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
            _ = LoadRecommendationsAsync();
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
    public async Task RequestExportAsync()
    {
        if (RequestExportRequested != null)
        {
            await RequestExportRequested.Invoke();
        }
    }

    [RelayCommand]
    public async Task RequestImportAsync()
    {
        if (RequestImportRequested != null)
        {
            await RequestImportRequested.Invoke();
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
        if (card.Id > 0)
        {
            var item = await Repository.GetByIdAsync(card.Id);
            if (item != null && OpenDetailRequested != null)
            {
                await OpenDetailRequested.Invoke(item);
            }
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
            _ = LoadRecommendationsAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshRecommendationsAsync()
    {
        await LoadRecommendationsAsync(forceRefresh: true);
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
        UpdateRecommendationVisibilityStates();
    }

    public async Task LoadRecommendationsAsync(bool forceRefresh = false, bool clearResults = false)
    {
        if (SelectedSection != NavigationSection.Recommendations) return;

        var requestId = ++_recommendationsRequestId;
        var targetPill = SelectedDiscoveryPill;
        var targetMediaType = SelectedMediaTypeFilter;

        _recommendationsCts?.Cancel();
        _recommendationsCts?.Dispose();
        _recommendationsCts = new CancellationTokenSource();
        var token = _recommendationsCts.Token;

        if (clearResults)
        {
            Recommendations.Clear();
            HasRecommendations = false;
        }

        IsLoadingRecommendations = true;
        HasRecommendationError = false;
        RecommendationErrorMessage = null;
        UpdateRecommendationVisibilityStates();

        if (forceRefresh)
        {
            JikanClient.ClearCache();
        }

        var genreFilter = string.Equals(SelectedRecommendationGenre, "All Genres", StringComparison.OrdinalIgnoreCase)
            ? null
            : SelectedRecommendationGenre;

        try
        {
            RecommendationResult result;
            if (targetPill == DiscoveryPill.Popular)
            {
                result = await RecommendationService.GetPopularRecommendationsAsync(
                    _allItems,
                    targetMediaType,
                    token);
            }
            else
            {
                result = await RecommendationService.GetExternalRecommendationsAsync(
                    _allItems,
                    targetMediaType,
                    genreFilter,
                    token);
            }

            if (token.IsCancellationRequested || requestId != _recommendationsRequestId || SelectedDiscoveryPill != targetPill)
            {
                return;
            }

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

            if (result.HasError)
            {
                HasRecommendationError = true;
                RecommendationErrorMessage = result.ErrorMessage;
                HasRecommendations = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Canceled by newer load request
        }
        catch (Exception ex)
        {
            if (requestId == _recommendationsRequestId && !token.IsCancellationRequested)
            {
                HasRecommendationError = true;
                RecommendationErrorMessage = $"Error loading recommendations: {ex.Message}";
                HasRecommendations = false;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested && requestId == _recommendationsRequestId)
            {
                IsLoadingRecommendations = false;
                UpdateRecommendationVisibilityStates();
            }
        }
    }

    private void UpdateRecommendationVisibilityStates()
    {
        ShowEmptyRecommendationState = !IsLoadingRecommendations && !HasRecommendationError && !HasRecommendations;
    }

    [RelayCommand]
    public async Task AddRecommendationToLibraryAsync(RecommendationCardViewModel? card)
    {
        if (card == null) return;
        var item = card.Recommendation.Item;

        // 1. Duplicate check via MalId and MalType if available
        if (item.MalId.HasValue && !string.IsNullOrWhiteSpace(item.MalType))
        {
            var existing = await Repository.GetByMalIdAsync(item.MalId.Value, item.MalType);
            if (existing != null)
            {
                Recommendations.Remove(card);
                HasRecommendations = Recommendations.Count > 0;
                UpdateRecommendationVisibilityStates();
                ShowRecommendationSuccess($"'{item.Title}' is already in your library.");
                return;
            }
        }

        var initialStatus = item.Type == MediaType.Anime ? MediaStatus.PlanToWatch : MediaStatus.PlanToRead;
        var newItem = new MediaItem
        {
            MalId = item.MalId,
            MalType = item.MalType,
            Title = item.Title,
            Type = item.Type,
            Status = initialStatus,
            CurrentProgress = 0,
            TotalProgress = item.TotalProgress,
            CoverImageUrl = item.CoverImageUrl,
            CoverImagePath = null,
            Genres = item.Genres,
            Synopsis = item.Synopsis,
            ReleaseYear = item.ReleaseYear,
            Score = null,
            RewatchCount = 0
        };

        try
        {
            await Repository.AddAsync(newItem);
            _allItems.Add(newItem);
            await RefreshCountsAsync();
            Recommendations.Remove(card);
            HasRecommendations = Recommendations.Count > 0;
            UpdateRecommendationVisibilityStates();
            ShowRecommendationSuccess($"Added '{newItem.Title}' to {initialStatus}.");
        }
        catch (Exception ex)
        {
            RecommendationErrorMessage = $"Failed to add title: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SelectDiscoveryTab(DiscoveryTab tab)
    {
        SelectedDiscoveryTab = tab;
    }

    [RelayCommand]
    public void ClearCatalogSearch()
    {
        CatalogSearchQuery = string.Empty;
    }

    [RelayCommand]
    public async Task SelectDiscoveryPillAsync(DiscoveryPill pill)
    {
        SelectedDiscoveryPill = pill;

        // Category switch: Immediately clear previous/intermediate results and show loading indicator
        Recommendations.Clear();
        HasRecommendations = false;
        IsLoadingRecommendations = true;
        HasRecommendationError = false;
        RecommendationErrorMessage = null;
        UpdateRecommendationVisibilityStates();

        _isChangingDiscoveryPill = true;
        try
        {
            switch (pill)
            {
                case DiscoveryPill.ForYou:
                case DiscoveryPill.Popular:
                    SelectedMediaTypeFilter = MediaTypeFilter.All;
                    CatalogSearchMediaType = MediaType.Anime;
                    break;
                case DiscoveryPill.Anime:
                    SelectedMediaTypeFilter = MediaTypeFilter.Anime;
                    CatalogSearchMediaType = MediaType.Anime;
                    break;
                case DiscoveryPill.Manga:
                    SelectedMediaTypeFilter = MediaTypeFilter.Manga;
                    CatalogSearchMediaType = MediaType.Manga;
                    break;
                case DiscoveryPill.Manhwa:
                    SelectedMediaTypeFilter = MediaTypeFilter.Manhwa;
                    CatalogSearchMediaType = MediaType.Manhwa;
                    break;
            }
        }
        finally
        {
            _isChangingDiscoveryPill = false;
        }

        if (IsShowingSearchResults)
        {
            await SearchCatalogNowAsync();
        }
        else
        {
            await LoadRecommendationsAsync(forceRefresh: false);
        }
    }

    [RelayCommand]
    public async Task SearchCatalogNowAsync()
    {
        _catalogSearchCts?.Cancel();
        _catalogSearchCts?.Dispose();
        _catalogSearchCts = new CancellationTokenSource();
        await PerformCatalogSearchAsync(CatalogSearchQuery, _catalogSearchCts.Token);
    }

    private async Task TriggerCatalogSearchDebounceAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await PerformCatalogSearchAsync(query, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Newer search requested
        }
    }

    public async Task PerformCatalogSearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var requestId = ++_catalogSearchRequestId;
        CatalogSearchErrorMessage = null;
        HasCatalogSearchError = false;

        if (string.IsNullOrWhiteSpace(query))
        {
            CatalogSearchResults.Clear();
            HasCatalogSearchResults = false;
            IsLoadingCatalogSearch = false;
            ShowEmptyCatalogSearchState = false;
            return;
        }

        CatalogSearchResults.Clear();
        HasCatalogSearchResults = false;
        IsLoadingCatalogSearch = true;
        ShowEmptyCatalogSearchState = false;

        try
        {
            var results = (CatalogSearchMediaType == MediaType.Anime)
                ? await JikanClient.SearchAnimeAsync(query, cancellationToken)
                : (CatalogSearchMediaType == MediaType.Manhwa
                    ? await JikanClient.SearchAsync("manhwa", query, cancellationToken)
                    : await JikanClient.SearchMangaAsync(query, cancellationToken));

            if (cancellationToken.IsCancellationRequested || requestId != _catalogSearchRequestId)
            {
                return;
            }

            CatalogSearchResults.Clear();

            var libraryItems = await Repository.GetAllAsync();
            var libraryMalIds = new HashSet<(int MalId, string MalType)>(
                libraryItems.Where(x => x.MalId.HasValue && !string.IsNullOrWhiteSpace(x.MalType))
                            .Select(x => (x.MalId!.Value, x.MalType!.ToLowerInvariant()))
            );

            foreach (var r in results)
            {
                var inLib = libraryMalIds.Contains((r.MalId, r.MalType.ToLowerInvariant()));
                var card = new CatalogSearchCardViewModel(r, CatalogSearchMediaType, inLib);
                card.LoadCoverImage();
                CatalogSearchResults.Add(card);
            }

            HasCatalogSearchResults = CatalogSearchResults.Count > 0;
            ShowEmptyCatalogSearchState = !HasCatalogSearchResults;
            if (ShowEmptyCatalogSearchState)
            {
                CatalogSearchEmptyMessage = $"No catalog results found for '{query}'.";
            }
            OnPropertyChanged(nameof(DiscoverySectionSubtitle));
        }
        catch (OperationCanceledException)
        {
            // Canceled by newer query
        }
        catch (JikanApiException ex) when (ex.IsRateLimited)
        {
            if (requestId == _catalogSearchRequestId && !cancellationToken.IsCancellationRequested)
            {
                CatalogSearchResults.Clear();
                HasCatalogSearchResults = false;
                HasCatalogSearchError = true;
                CatalogSearchErrorMessage = "Jikan API rate limit reached (HTTP 429). Please wait a moment before searching again.";
            }
        }
        catch (Exception ex)
        {
            if (requestId == _catalogSearchRequestId && !cancellationToken.IsCancellationRequested)
            {
                CatalogSearchResults.Clear();
                HasCatalogSearchResults = false;
                HasCatalogSearchError = true;
                CatalogSearchErrorMessage = $"Error searching catalog: {ex.Message}";
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && requestId == _catalogSearchRequestId)
            {
                IsLoadingCatalogSearch = false;
            }
        }
    }

    [RelayCommand]
    public async Task AddCatalogSearchResultToLibraryAsync(CatalogSearchCardViewModel? card)
    {
        if (card == null) return;
        if (card.IsInLibrary)
        {
            ShowRecommendationSuccess($"'{card.Title}' is already in your library.");
            return;
        }

        var existing = await Repository.GetByMalIdAsync(card.MalId, card.MalType);
        if (existing != null)
        {
            card.IsInLibrary = true;
            ShowRecommendationSuccess($"'{card.Title}' is already in your library.");
            return;
        }

        var initialStatus = card.Type == MediaType.Anime ? MediaStatus.PlanToWatch : MediaStatus.PlanToRead;
        var newItem = new MediaItem
        {
            MalId = card.MalId,
            MalType = card.MalType,
            Title = !string.IsNullOrWhiteSpace(card.EnglishTitle) ? card.EnglishTitle : card.Title,
            Type = card.Type,
            Status = initialStatus,
            CurrentProgress = 0,
            TotalProgress = card.TotalProgress,
            CoverImageUrl = card.CoverImageUrl,
            CoverImagePath = null,
            Genres = card.Genres.Count > 0 ? string.Join(", ", card.Genres) : null,
            Synopsis = card.Synopsis,
            ReleaseYear = card.ReleaseYear,
            Score = null,
            RewatchCount = 0
        };

        try
        {
            await Repository.AddAsync(newItem);
            _allItems.Add(newItem);
            await RefreshCountsAsync();
            card.IsInLibrary = true;
            ShowRecommendationSuccess($"Added '{newItem.Title}' to {initialStatus}.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            card.IsInLibrary = true;
            ShowRecommendationSuccess($"'{card.Title}' is already in your library.");
        }
        catch (Exception ex)
        {
            RecommendationErrorMessage = $"Failed to add title: {ex.Message}";
        }
    }

    public void ShowRecommendationSuccess(string message, int durationMs = 3000)
    {
        _successMessageCts?.Cancel();
        _successMessageCts?.Dispose();
        _successMessageCts = new CancellationTokenSource();
        var token = _successMessageCts.Token;

        RecommendationSuccessMessage = message;

        _ = DismissSuccessMessageAfterDelayAsync(durationMs, token);
    }

    private async Task DismissSuccessMessageAfterDelayAsync(int delayMs, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delayMs, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                RecommendationSuccessMessage = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
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

    public async Task ExportToFileAsync(string filePath)
    {
        try
        {
            BackupErrorMessage = null;
            BackupStatusMessage = null;
            var items = await Repository.GetAllAsync();
            await BackupService.ExportToFileAsync(filePath, items);
            BackupStatusMessage = $"Library successfully exported ({items.Count} items) to {System.IO.Path.GetFileName(filePath)}.";
        }
        catch (Exception ex)
        {
            BackupErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    public async Task PrepareImportFromFileAsync(string filePath)
    {
        try
        {
            BackupErrorMessage = null;
            BackupStatusMessage = null;
            IsImportConfirming = false;
            PendingImportItems = null;

            var result = await BackupService.ValidateFileAsync(filePath);
            if (!result.IsValid || result.ValidatedItems == null)
            {
                BackupErrorMessage = $"Import validation failed: {result.ErrorMessage}";
                return;
            }

            PendingImportItems = result.ValidatedItems.ToList();
            ValidatedImportCount = PendingImportItems.Count;
            IsImportConfirming = true;
            BackupStatusMessage = $"File validated: {ValidatedImportCount} valid items found. Confirmation required to replace library.";
        }
        catch (Exception ex)
        {
            BackupErrorMessage = $"Failed to validate file: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CancelImport()
    {
        PendingImportItems = null;
        IsImportConfirming = false;
        ValidatedImportCount = 0;
        BackupStatusMessage = "Import cancelled. No changes were made to your library.";
    }

    [RelayCommand]
    public async Task ConfirmImportAsync()
    {
        if (PendingImportItems == null || PendingImportItems.Count == 0)
        {
            IsImportConfirming = false;
            return;
        }

        try
        {
            BackupErrorMessage = null;
            var count = PendingImportItems.Count;
            await BackupService.ImportReplaceAsync(PendingImportItems);
            PendingImportItems = null;
            IsImportConfirming = false;
            ValidatedImportCount = 0;
            await LoadLibraryAsync();
            BackupStatusMessage = $"Successfully imported {count} items. Your library has been replaced.";
        }
        catch (Exception ex)
        {
            BackupErrorMessage = $"Database transaction failed during import: {ex.Message}";
        }
    }
}
