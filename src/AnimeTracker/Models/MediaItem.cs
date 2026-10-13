using System;

namespace AnimeTracker.Models;

public enum MediaType
{
    Anime,
    Manga,
    Manhwa
}

public enum MediaStatus
{
    Watching,
    Reading,
    PlanToWatch,
    PlanToRead,
    Completed,
    OnHold,
    Dropped
}

public class MediaItem
{
    public int Id { get; set; }
    public int? MalId { get; set; }
    public string? MalType { get; set; }
    public string Title { get; set; } = string.Empty;
    public MediaType Type { get; set; }
    public MediaStatus Status { get; set; }
    public int CurrentProgress { get; set; }
    public int? TotalProgress { get; set; }
    public int? Score { get; set; }
    public int RewatchCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? CoverImagePath { get; set; }
    public byte[]? PendingCoverImageData { get; set; }
    public string? Genres { get; set; }
    public string? Synopsis { get; set; }
    public int? ReleaseYear { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool TrySetProgress(int newProgress)
    {
        if (newProgress < 0) return false;
        if (TotalProgress.HasValue && newProgress > TotalProgress.Value) return false;
        CurrentProgress = newProgress;
        if (TotalProgress.HasValue && TotalProgress.Value > 0 && CurrentProgress >= TotalProgress.Value)
        {
            Status = MediaStatus.Completed;
        }
        return true;
    }

    public bool TryIncrementProgress()
    {
        if (CurrentProgress < 0)
        {
            CurrentProgress = 0;
        }

        if (TotalProgress.HasValue && CurrentProgress >= TotalProgress.Value)
        {
            return false;
        }

        CurrentProgress++;
        if (TotalProgress.HasValue && TotalProgress.Value > 0 && CurrentProgress >= TotalProgress.Value)
        {
            Status = MediaStatus.Completed;
        }
        return true;
    }

    public bool TryDecrementProgress()
    {
        if (CurrentProgress <= 0)
        {
            CurrentProgress = 0;
            return false;
        }

        CurrentProgress--;
        return true;
    }

    public void MarkCompleted()
    {
        Status = MediaStatus.Completed;
        if (TotalProgress.HasValue)
        {
            CurrentProgress = TotalProgress.Value;
        }
    }

    public void StartRewatch()
    {
        if (Status != MediaStatus.Completed)
        {
            throw new InvalidOperationException("Cannot start rewatch/reread on a title that is not Completed.");
        }

        RewatchCount = Math.Max(0, RewatchCount) + 1;
        CurrentProgress = 0;
        Status = Type == MediaType.Anime ? MediaStatus.Watching : MediaStatus.Reading;
    }
}
