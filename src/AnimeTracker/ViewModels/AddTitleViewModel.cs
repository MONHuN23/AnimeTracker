using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services.Jikan;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AnimeTracker.ViewModels;

public partial class AddTitleViewModel : ViewModelBase
{
    private readonly MediaItemRepository _repository;
    private readonly JikanApiClient _jikanClient;
    private CancellationTokenSource? _searchCts;

    public event EventHandler? RequestClose;

    // Search catalog properties
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private MediaType _searchMediaType = MediaType.Anime;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private string? _searchErrorMessage;

    [ObservableProperty]
    private string? _actionMessage;

    [ObservableProperty]
    private JikanSearchResult? _selectedSearchResult;

    [ObservableProperty]
    private bool _wasAddedSuccessfully;

    public ObservableCollection<JikanSearchResult> SearchResults { get; } = new();

    // Manual entry properties
    [ObservableProperty]
    private string _manualTitle = string.Empty;

    [ObservableProperty]
    private MediaType _manualMediaType = MediaType.Anime;

    [ObservableProperty]
    private int? _manualTotalProgress;

    [ObservableProperty]
    private string? _manualGenres;

    [ObservableProperty]
    private string? _manualSynopsis;

    [ObservableProperty]
    private string? _manualErrorMessage;

    [ObservableProperty]
    private string? _manualWarningMessage;

    [ObservableProperty]
    private bool _allowDuplicateTitleOverride;

    public AddTitleViewModel(MediaItemRepository repository, JikanApiClient jikanClient)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _jikanClient = jikanClient ?? throw new ArgumentNullException(nameof(jikanClient));
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = TriggerDebounceAsync(value, _searchCts.Token);
    }

    partial void OnSearchMediaTypeChanged(MediaType value)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            _ = TriggerDebounceAsync(SearchQuery, _searchCts.Token);
        }
    }

    private async Task TriggerDebounceAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            await PerformSearchAsync(query, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Debounced request superseded by a newer one
        }
    }

    public async Task PerformSearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        ActionMessage = null;
        SearchErrorMessage = null;

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults.Clear();
            IsLoading = false;
            HasSearched = false;
            return;
        }

        IsLoading = true;
        HasSearched = true;

        try
        {
            var results = (SearchMediaType == MediaType.Anime)
                ? await _jikanClient.SearchAnimeAsync(query, cancellationToken)
                : await _jikanClient.SearchMangaAsync(query, cancellationToken);

            SearchResults.Clear();
            foreach (var r in results)
            {
                SearchResults.Add(r);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled request
        }
        catch (JikanApiException ex) when (ex.IsRateLimited)
        {
            SearchResults.Clear();
            SearchErrorMessage = "Jikan API rate limit reached (HTTP 429). Please wait a moment before searching again.";
        }
        catch (Exception ex)
        {
            SearchResults.Clear();
            SearchErrorMessage = $"Error searching catalog: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task<bool> AddSelectedCatalogTitleAsync()
    {
        ActionMessage = null;

        if (SelectedSearchResult == null)
        {
            ActionMessage = "Please select a title from the results first.";
            return false;
        }

        // Duplicate check via (MalId, MalType)
        var existing = await _repository.GetByMalIdAsync(SelectedSearchResult.MalId, SelectedSearchResult.MalType);
        if (existing != null)
        {
            ActionMessage = "This title is already in your library.";
            return false;
        }

        var item = new MediaItem
        {
            MalId = SelectedSearchResult.MalId,
            MalType = SelectedSearchResult.MalType,
            Title = !string.IsNullOrWhiteSpace(SelectedSearchResult.EnglishTitle)
                ? SelectedSearchResult.EnglishTitle
                : SelectedSearchResult.Title,
            Type = SearchMediaType,
            Status = SearchMediaType == MediaType.Anime ? MediaStatus.PlanToWatch : MediaStatus.PlanToRead,
            CurrentProgress = 0,
            TotalProgress = SelectedSearchResult.TotalCount,
            CoverImageUrl = SelectedSearchResult.CoverImageUrl,
            Genres = SelectedSearchResult.Genres.Count > 0 ? string.Join(", ", SelectedSearchResult.Genres) : null,
            Synopsis = SelectedSearchResult.Synopsis,
            ReleaseYear = SelectedSearchResult.Year,
            Score = null,
            RewatchCount = 0
        };

        try
        {
            await _repository.AddAsync(item);
            WasAddedSuccessfully = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ActionMessage = "This title is already in your library.";
            return false;
        }
        catch (Exception ex)
        {
            ActionMessage = $"Failed to add title: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public async Task<bool> AddManualTitleAsync()
    {
        ManualErrorMessage = null;

        if (string.IsNullOrWhiteSpace(ManualTitle))
        {
            ManualErrorMessage = "Title is required.";
            return false;
        }

        if (ManualTotalProgress.HasValue && ManualTotalProgress.Value < 0)
        {
            ManualErrorMessage = "Total progress cannot be negative.";
            return false;
        }

        // Soft duplicate check for manual title name
        var all = await _repository.GetAllAsync();
        var existing = all.FirstOrDefault(x => string.Equals(x.Title.Trim(), ManualTitle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing != null && !AllowDuplicateTitleOverride)
        {
            ManualWarningMessage = $"A title named '{existing.Title}' already exists in your library. Click Add again to confirm.";
            AllowDuplicateTitleOverride = true;
            return false;
        }

        var item = new MediaItem
        {
            MalId = null,
            MalType = null,
            Title = ManualTitle.Trim(),
            Type = ManualMediaType,
            Status = ManualMediaType == MediaType.Anime ? MediaStatus.PlanToWatch : MediaStatus.PlanToRead,
            CurrentProgress = 0,
            TotalProgress = ManualTotalProgress,
            Genres = string.IsNullOrWhiteSpace(ManualGenres) ? null : ManualGenres.Trim(),
            Synopsis = string.IsNullOrWhiteSpace(ManualSynopsis) ? null : ManualSynopsis.Trim(),
            Score = null,
            RewatchCount = 0
        };

        try
        {
            await _repository.AddAsync(item);
            WasAddedSuccessfully = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ManualErrorMessage = $"Failed to save title: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
