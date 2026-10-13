using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Jikan;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AnimeTracker.ViewModels;

public partial class AddTitleViewModel : ViewModelBase
{
    private readonly MediaItemRepository _repository;
    private readonly JikanApiClient _jikanClient;
    private readonly CoverStorageService _coverStorageService;
    private CancellationTokenSource? _searchCts;

    public event EventHandler? RequestClose;
    public Func<Task<string?>>? RequestSelectImageFile { get; set; }

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
    private int? _manualCurrentProgress = 0;

    [ObservableProperty]
    private int? _manualTotalProgress;

    [ObservableProperty]
    private string? _manualGenres;

    [ObservableProperty]
    private string? _manualSynopsis;

    [ObservableProperty]
    private string? _manualCoverFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualCover))]
    private Bitmap? _manualCoverPreview;

    [ObservableProperty]
    private string? _manualErrorMessage;

    [ObservableProperty]
    private string? _manualWarningMessage;

    [ObservableProperty]
    private bool _allowDuplicateTitleOverride;

    public bool HasManualCover => ManualCoverPreview != null;

    public AddTitleViewModel(
        MediaItemRepository repository,
        JikanApiClient jikanClient,
        CoverStorageService? coverStorageService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _jikanClient = jikanClient ?? throw new ArgumentNullException(nameof(jikanClient));
        _coverStorageService = coverStorageService ?? new CoverStorageService();
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
            _ = PerformSearchAsync(SearchQuery, _searchCts.Token);
        }
    }

    [RelayCommand]
    public async Task SearchNowAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        await PerformSearchAsync(SearchQuery, _searchCts.Token);
    }

    private async Task TriggerDebounceAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
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
                r.LoadCoverImage();
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
    public async Task ChooseImageAsync()
    {
        if (RequestSelectImageFile != null)
        {
            var path = await RequestSelectImageFile.Invoke();
            if (!string.IsNullOrWhiteSpace(path))
            {
                SetSelectedCoverFile(path);
            }
        }
    }

    [RelayCommand]
    public void RemoveImage()
    {
        ManualCoverFilePath = null;
        ManualCoverPreview = null;
    }

    public void SetSelectedCoverFile(string path)
    {
        if (!File.Exists(path))
        {
            ManualErrorMessage = "The selected file does not exist.";
            return;
        }

        if (!_coverStorageService.ValidateFile(path, out var error))
        {
            ManualErrorMessage = error;
            return;
        }

        var bitmap = ImageLoader.LoadFromFile(path);
        if (bitmap == null)
        {
            ManualErrorMessage = "Failed to load image preview.";
            return;
        }

        ManualCoverFilePath = path;
        ManualCoverPreview = bitmap;
        ManualErrorMessage = null;
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

        var effectiveProgress = ManualCurrentProgress ?? 0;

        if (ManualTotalProgress.HasValue && ManualTotalProgress.Value < 0)
        {
            ManualErrorMessage = "Total progress cannot be negative.";
            return false;
        }

        if (effectiveProgress < 0)
        {
            ManualErrorMessage = "Progress cannot be negative.";
            return false;
        }

        if (ManualTotalProgress.HasValue && effectiveProgress > ManualTotalProgress.Value)
        {
            ManualErrorMessage = "Progress cannot exceed total progress.";
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

        var initialStatus = effectiveProgress > 0
            ? (ManualMediaType == MediaType.Anime ? MediaStatus.Watching : MediaStatus.Reading)
            : (ManualMediaType == MediaType.Anime ? MediaStatus.PlanToWatch : MediaStatus.PlanToRead);

        if (ManualTotalProgress.HasValue && ManualTotalProgress.Value > 0 && effectiveProgress >= ManualTotalProgress.Value)
        {
            initialStatus = MediaStatus.Completed;
        }

        string? savedCoverPath = null;
        if (!string.IsNullOrWhiteSpace(ManualCoverFilePath))
        {
            try
            {
                savedCoverPath = await _coverStorageService.SaveCoverFromFileAsync(ManualCoverFilePath);
            }
            catch (Exception ex)
            {
                ManualErrorMessage = $"Failed to save cover image: {ex.Message}";
                return false;
            }
        }

        var item = new MediaItem
        {
            MalId = null,
            MalType = null,
            Title = ManualTitle.Trim(),
            Type = ManualMediaType,
            Status = initialStatus,
            CurrentProgress = effectiveProgress,
            TotalProgress = ManualTotalProgress,
            CoverImagePath = savedCoverPath,
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
            if (savedCoverPath != null)
            {
                _coverStorageService.DeleteCover(savedCoverPath);
            }
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
