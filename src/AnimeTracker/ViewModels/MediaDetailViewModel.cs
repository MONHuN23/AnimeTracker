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

    public event EventHandler? RequestClose;

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
    private MediaStatus _status;

    [ObservableProperty]
    private int _currentProgress;

    [ObservableProperty]
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

    public string RewatchActionLabel => Type == MediaType.Anime ? "Start Rewatch" : "Start Reread";

    public string TypeDisplay => Type.ToString().ToUpperInvariant();

    public string ProgressDisplay =>
        Type == MediaType.Anime
            ? (TotalProgress.HasValue ? $"EP {CurrentProgress} / {TotalProgress}" : $"EP {CurrentProgress}")
            : (TotalProgress.HasValue ? $"CH {CurrentProgress} / {TotalProgress}" : $"CH {CurrentProgress}");

    public MediaDetailViewModel(
        MediaItem item,
        MediaItemRepository repository,
        MediaTrackingService trackingService)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));

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

    partial void OnStatusChanged(MediaStatus value)
    {
        OnPropertyChanged(nameof(CanRewatch));
    }

    partial void OnCurrentProgressChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressDisplay));
    }

    partial void OnTotalProgressChanged(int? value)
    {
        OnPropertyChanged(nameof(ProgressDisplay));
    }

    public void LoadCoverImage()
    {
        if (CoverImage == null && !string.IsNullOrWhiteSpace(CoverImageUrl))
        {
            _ = Task.Run(async () =>
            {
                var bmp = await ImageLoader.LoadFromUrlAsync(CoverImageUrl);
                if (bmp != null)
                {
                    try
                    {
                        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                        {
                            CoverImage = bmp;
                        }
                        else
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => CoverImage = bmp);
                        }
                    }
                    catch
                    {
                        CoverImage = bmp;
                    }
                }
            });
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

        if (TotalProgress.HasValue && TotalProgress.Value < 0)
        {
            ErrorMessage = "Total progress cannot be negative.";
            return false;
        }

        if (TotalProgress.HasValue && TotalProgress.Value < CurrentProgress)
        {
            ErrorMessage = $"Total progress ({TotalProgress.Value}) cannot be less than current progress ({CurrentProgress}).";
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

        Item.Title = Title.Trim();
        Item.Status = Status;
        Item.Score = Score;
        Item.TotalProgress = TotalProgress;
        Item.Genres = string.IsNullOrWhiteSpace(Genres) ? null : Genres.Trim();
        Item.Synopsis = string.IsNullOrWhiteSpace(Synopsis) ? null : Synopsis.Trim();

        var success = await _repository.UpdateAsync(Item);
        if (success)
        {
            WasSavedSuccessfully = true;
            HasUnsavedChangesOrActionOccurred = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
            return true;
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

    [RelayCommand]
    public async Task StartRewatchAsync()
    {
        if (Status != MediaStatus.Completed)
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
