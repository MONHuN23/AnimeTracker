using System;
using System.Threading.Tasks;
using AnimeTracker.Models;
using AnimeTracker.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnimeTracker.ViewModels;

public partial class MediaCardViewModel : ViewModelBase
{
    private readonly MediaTrackingService _trackingService;
    private readonly Func<MediaCardViewModel, Task>? _onCardUpdated;

    public int Id { get; }
    public string Title { get; }
    public MediaType Type { get; }
    public string? CoverImageUrl { get; }
    public int? TotalProgress { get; }
    public int? Score { get; }

    [ObservableProperty]
    private MediaStatus _status;

    [ObservableProperty]
    private int _currentProgress;

    [ObservableProperty]
    private DateTime _updatedAt;

    [ObservableProperty]
    private Bitmap? _coverImage;

    public string TypeDisplay => Type.ToString().ToUpperInvariant();

    public string ProgressDisplay =>
        Type == MediaType.Anime
            ? (TotalProgress.HasValue ? $"EP {CurrentProgress} / {TotalProgress}" : $"EP {CurrentProgress}")
            : (TotalProgress.HasValue ? $"CH {CurrentProgress} / {TotalProgress}" : $"CH {CurrentProgress}");

    public string? ScoreDisplay => Score.HasValue ? $"★ {Score}/10" : null;

    public MediaCardViewModel(
        MediaItem item,
        MediaTrackingService trackingService,
        Func<MediaCardViewModel, Task>? onCardUpdated = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        _trackingService = trackingService ?? throw new ArgumentNullException(nameof(trackingService));
        _onCardUpdated = onCardUpdated;

        Id = item.Id;
        Title = item.Title;
        Type = item.Type;
        CoverImageUrl = item.CoverImageUrl;
        TotalProgress = item.TotalProgress;
        Score = item.Score;

        _status = item.Status;
        _currentProgress = item.CurrentProgress;
        _updatedAt = item.UpdatedAt;
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
    public async Task IncrementProgressAsync()
    {
        var updated = await _trackingService.IncrementProgressAsync(Id);
        CurrentProgress = updated.CurrentProgress;
        Status = updated.Status;
        UpdatedAt = updated.UpdatedAt;
        OnPropertyChanged(nameof(ProgressDisplay));

        if (_onCardUpdated != null)
        {
            await _onCardUpdated.Invoke(this);
        }
    }

    [RelayCommand]
    public async Task DecrementProgressAsync()
    {
        var updated = await _trackingService.DecrementProgressAsync(Id);
        CurrentProgress = updated.CurrentProgress;
        Status = updated.Status;
        UpdatedAt = updated.UpdatedAt;
        OnPropertyChanged(nameof(ProgressDisplay));

        if (_onCardUpdated != null)
        {
            await _onCardUpdated.Invoke(this);
        }
    }
}
