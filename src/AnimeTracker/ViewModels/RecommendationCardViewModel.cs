using System;
using System.Threading.Tasks;
using AnimeTracker.Models;
using AnimeTracker.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnimeTracker.ViewModels;

public partial class RecommendationCardViewModel : ViewModelBase
{
    public RecommendationItem Recommendation { get; }
    public MediaItem Item => Recommendation.Item;

    public int Id => Item.Id;
    public string Title => Item.Title;
    public MediaType Type => Item.Type;
    public string TypeDisplay => Type.ToString().ToUpperInvariant();
    public string? CoverImageUrl => Item.CoverImageUrl;
    public string? CoverImagePath => Item.CoverImagePath;
    public string? Genres => Item.Genres;
    public string Reason => Recommendation.Reason;
    public double MatchScore => Recommendation.MatchScore;
    public string MatchScoreDisplay => $"★ Score {MatchScore:F0}";
    public int? TotalProgress => Item.TotalProgress;

    public string ProgressDisplay =>
        Type == MediaType.Anime
            ? (TotalProgress.HasValue ? $"{TotalProgress} Episodes" : "Episodes unknown")
            : (TotalProgress.HasValue ? $"{TotalProgress} Chapters" : "Chapters unknown");

    public bool IsInLibrary => Id > 0;
    public double? ExternalScore => Recommendation.ExternalScore;
    public string? ExternalScoreDisplay => ExternalScore.HasValue ? $"★ {ExternalScore.Value:F1}" : null;
    public string? Synopsis => Item.Synopsis;
    public int? ReleaseYear => Item.ReleaseYear;

    [ObservableProperty]
    private Bitmap? _coverImage;

    public RecommendationCardViewModel(RecommendationItem recommendation)
    {
        Recommendation = recommendation ?? throw new ArgumentNullException(nameof(recommendation));
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
}
