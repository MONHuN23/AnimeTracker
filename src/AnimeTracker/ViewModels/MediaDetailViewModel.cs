using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnimeTracker.ViewModels;

public partial class MediaDetailViewModel : ViewModelBase
{
    private readonly MediaItemRepository _repository;
    private readonly MediaTrackingService _trackingService;
    private readonly CoverStorageService _coverStorageService;

    public event EventHandler? RequestClose;
    public Func<Task<string?>>? RequestSelectImageFile { get; set; }

    public MediaItem Item { get; }
    public int Id => Item.Id;
    public MediaType Type => Item.Type;
    public int? MalId => Item.MalId;
    public string? MalType => Item.MalType;
    public int? ReleaseYear => Item.ReleaseYear;
    public string? CoverImageUrl => Item.CoverImageUrl;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRewatch))]
    [NotifyCanExecuteChangedFor(nameof(StartRewatchCommand))]
    private MediaStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    private int? _currentProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    private int? _totalProgress;

    [ObservableProperty]
    private int? _score;

    [ObservableProperty]
    private string? _genres;

    [ObservableProperty]
    private string? _synopsis;

    [ObservableProperty]
    private int _rewatchCount;

    [ObservableProperty]
    private DateTime _updatedAt;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private string? _coverImagePath;

    private string? _pendingNewCoverFilePath;
    private bool _isCustomCoverRemoved;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private bool _wasSavedSuccessfully;

    [ObservableProperty]
    private bool _wasDeleted;

    [ObservableProperty]
    private bool _hasUnsavedChangesOrActionOccurred;

    public IReadOnlyList<MediaStatus> AvailableStatuses { get; }

    public bool CanRewatch => Status == MediaStatus.Completed;

    public bool HasCustomCover => !string.IsNullOrWhiteSpace(CoverImagePath) || !string.IsNullOrWhiteSpace(_pendingNewCoverFilePath);

    public string ProgressUnitLabel => Type == MediaType.Anime ? "Episode" : "Chapter";

    public string CurrentProgressUnitLabel => Type == MediaType.Anime ? "Current Episode" : "Current Chapter";

    public string RewatchActionLabel => Type == MediaType.Anime ? "Start Rewatch" : "Start Reread";

    public string TypeDisplay => Type.ToString().ToUpperInvariant();

    public string ProgressDisplay
    {
        get
        {
            var cur = CurrentProgress ?? 0;
            return Type == MediaType.Anime
                ? (TotalProgress.HasValue ? $"EP {cur} / {TotalProgress}" : $"EP {cur}")
                : (TotalProgress.HasValue ? $"CH {cur} / {TotalProgress}" : $"CH {cur}");
        }
    }

    public MediaDetailViewModel(
        MediaItem item,
        MediaItemRepository repository,
        MediaTrackingService trackingService,
        CoverStorageService? coverStorageService = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));
        _coverStorageService = coverStorageService ?? new CoverStorageService();

        _title = item.Title;
        _status = item.Status;
        _currentProgress = item.CurrentProgress;
        _totalProgress = item.TotalProgress;
        _score = item.Score;
        _genres = item.Genres;
        _synopsis = item.Synopsis;
        _rewatchCount = item.RewatchCount;
        _updatedAt = item.UpdatedAt;
        _createdAt = item.CreatedAt;
        _coverImagePath = item.CoverImagePath;

        if (item.Type == MediaType.Anime)
        {
            AvailableStatuses = new List<MediaStatus>
            {
                MediaStatus.Watching,
                MediaStatus.PlanToWatch,
                MediaStatus.Completed,
                MediaStatus.OnHold,
                MediaStatus.Dropped
            };
        }
        else
        {
            AvailableStatuses = new List<MediaStatus>
            {
                MediaStatus.Reading,
                MediaStatus.PlanToRead,
                MediaStatus.Completed,
                MediaStatus.OnHold,
                MediaStatus.Dropped
            };
        }
    }

    public void LoadCoverImage()
    {
        _ = LoadCoverImageAsync();
    }

    public async Task LoadCoverImageAsync()
    {
        if (CoverImage == null && (!string.IsNullOrWhiteSpace(CoverImagePath) || !string.IsNullOrWhiteSpace(CoverImageUrl)))
        {
            var bmp = await ImageLoader.LoadCoverAsync(CoverImagePath, CoverImageUrl);
            if (bmp != null)
            {
                CoverImage = bmp;
            }
        }
    }

    [RelayCommand]
    public async Task ChooseCoverImageAsync()
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

    public void SetSelectedCoverFile(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            ErrorMessage = "The selected file does not exist.";
            return;
        }

        if (!_coverStorageService.ValidateFile(path, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var bitmap = ImageLoader.LoadFromFile(path);
        if (bitmap == null)
        {
            ErrorMessage = "Failed to load image preview.";
            return;
        }

        _pendingNewCoverFilePath = path;
        _isCustomCoverRemoved = false;
        CoverImage = bitmap;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasCustomCover));
    }

    [RelayCommand]
    public void RemoveCustomCover()
    {
        _pendingNewCoverFilePath = null;
        _isCustomCoverRemoved = true;
        CoverImagePath = null;
        CoverImage = null;
        OnPropertyChanged(nameof(HasCustomCover));

        if (!string.IsNullOrWhiteSpace(CoverImageUrl))
        {
            _ = LoadCoverImageAsync();
        }
    }

    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title cannot be empty.";
            return false;
        }

        if (Score.HasValue && (Score.Value < 1 || Score.Value > 10))
        {
            ErrorMessage = "Score must be between 1 and 10.";
            return false;
        }

        var effectiveProgress = CurrentProgress ?? 0;

        if (effectiveProgress < 0)
        {
            ErrorMessage = "Progress cannot be negative.";
            return false;
        }

        if (TotalProgress.HasValue && effectiveProgress > TotalProgress.Value)
        {
            ErrorMessage = $"Current progress ({effectiveProgress}) cannot exceed total progress ({TotalProgress.Value}).";
            return false;
        }

        if (TotalProgress.HasValue && TotalProgress.Value < 0)
        {
            ErrorMessage = "Total progress cannot be negative.";
            return false;
        }

        if (TotalProgress.HasValue && TotalProgress.Value < effectiveProgress)
        {
            ErrorMessage = $"Total progress ({TotalProgress.Value}) cannot be less than current progress ({effectiveProgress}).";
            return false;
        }

        if (Type == MediaType.Anime && (Status == MediaStatus.Reading || Status == MediaStatus.PlanToRead))
        {
            ErrorMessage = "Reading / Plan to Read statuses are not valid for Anime.";
            return false;
        }

        if ((Type == MediaType.Manga || Type == MediaType.Manhwa) && (Status == MediaStatus.Watching || Status == MediaStatus.PlanToWatch))
        {
            ErrorMessage = "Watching / Plan to Watch statuses are not valid for Manga or Manhwa.";
            return false;
        }

        if (TotalProgress.HasValue && TotalProgress.Value > 0 && effectiveProgress >= TotalProgress.Value)
        {
            Status = MediaStatus.Completed;
        }

        string? previousCoverToDelete = null;
        string? newlySavedCoverPath = null;

        if (!string.IsNullOrWhiteSpace(_pendingNewCoverFilePath))
        {
            try
            {
                newlySavedCoverPath = await _coverStorageService.SaveCoverFromFileAsync(_pendingNewCoverFilePath);
                if (!string.IsNullOrWhiteSpace(Item.CoverImagePath))
                {
                    previousCoverToDelete = Item.CoverImagePath;
                }
                Item.CoverImagePath = newlySavedCoverPath;
                CoverImagePath = newlySavedCoverPath;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to save cover image: {ex.Message}";
                return false;
            }
        }
        else if (_isCustomCoverRemoved)
        {
            if (!string.IsNullOrWhiteSpace(Item.CoverImagePath))
            {
                previousCoverToDelete = Item.CoverImagePath;
            }
            Item.CoverImagePath = null;
            CoverImagePath = null;
        }

        Item.Title = Title.Trim();
        Item.Status = Status;
        Item.CurrentProgress = effectiveProgress;
        CurrentProgress = effectiveProgress;
        Item.Score = Score;
        Item.TotalProgress = TotalProgress;
        Item.Genres = string.IsNullOrWhiteSpace(Genres) ? null : Genres.Trim();
        Item.Synopsis = string.IsNullOrWhiteSpace(Synopsis) ? null : Synopsis.Trim();

        var success = await _repository.UpdateAsync(Item);
        if (success)
        {
            if (previousCoverToDelete != null)
            {
                _coverStorageService.DeleteCover(previousCoverToDelete);
            }
            _pendingNewCoverFilePath = null;
            _isCustomCoverRemoved = false;
            WasSavedSuccessfully = true;
            HasUnsavedChangesOrActionOccurred = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (newlySavedCoverPath != null)
        {
            _coverStorageService.DeleteCover(newlySavedCoverPath);
        }

        ErrorMessage = "Failed to update item in database.";
        return false;
    }

    [RelayCommand]
    public async Task MarkCompletedAsync()
    {
        try
        {
            var updated = await _trackingService.CompleteAsync(Id);
            Status = updated.Status;
            CurrentProgress = updated.CurrentProgress;
            UpdatedAt = updated.UpdatedAt;
            HasUnsavedChangesOrActionOccurred = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark completed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRewatch))]
    public async Task StartRewatchAsync()
    {
        if (!CanRewatch)
        {
            ErrorMessage = "Cannot start rewatch/reread on a title that is not Completed.";
            return;
        }

        try
        {
            var updated = await _trackingService.StartRewatchAsync(Id);
            Status = updated.Status;
            CurrentProgress = updated.CurrentProgress;
            RewatchCount = updated.RewatchCount;
            UpdatedAt = updated.UpdatedAt;
            HasUnsavedChangesOrActionOccurred = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start rewatch/reread: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task IncrementProgressAsync()
    {
        try
        {
            var updated = await _trackingService.IncrementProgressAsync(Id);
            CurrentProgress = updated.CurrentProgress;
            Status = updated.Status;
            UpdatedAt = updated.UpdatedAt;
            HasUnsavedChangesOrActionOccurred = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to increment progress: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DecrementProgressAsync()
    {
        try
        {
            var updated = await _trackingService.DecrementProgressAsync(Id);
            CurrentProgress = updated.CurrentProgress;
            Status = updated.Status;
            UpdatedAt = updated.UpdatedAt;
            HasUnsavedChangesOrActionOccurred = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to decrement progress: {ex.Message}";
        }
    }

    [RelayCommand]
    public void RequestDelete()
    {
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    public void CancelDelete()
    {
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    public async Task<bool> ConfirmDeleteAsync()
    {
        try
        {
            var deleted = await _repository.DeleteAsync(Id);
            if (deleted)
            {
                WasDeleted = true;
                HasUnsavedChangesOrActionOccurred = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
                return true;
            }

            ErrorMessage = "Failed to delete item.";
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete item: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public void ClearScore()
    {
        Score = null;
    }

    [RelayCommand]
    public void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
