using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Jikan;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnimeTracker.ViewModels;

public partial class CatalogSearchCardViewModel : ViewModelBase
{
    public JikanSearchResult Result { get; }
    public MediaType Type { get; }

    public int MalId => Result.MalId;
    public string MalType => Result.MalType;
    public string Title => Result.Title;
    public string? EnglishTitle => Result.EnglishTitle;
    public string? CoverImageUrl => Result.CoverImageUrl;
    public int? TotalProgress => Result.TotalCount;
    public string? Synopsis => Result.Synopsis;
    public IReadOnlyList<string> Genres => Result.Genres;
    public string GenresDisplay => Genres.Count > 0 ? string.Join(", ", Genres) : string.Empty;
    public double? Score => Result.Score;
    public int? ReleaseYear => Result.Year;

    public string TypeDisplay => Type.ToString().ToUpperInvariant();
    public string? ScoreDisplay => Score.HasValue ? $"★ {Score.Value:F1}" : null;
    public string ProgressDisplay =>
        Type == MediaType.Anime
            ? (TotalProgress.HasValue ? $"{TotalProgress} Episodes" : "Episodes unknown")
            : (TotalProgress.HasValue ? $"{TotalProgress} Chapters" : "Chapters unknown");

    [ObservableProperty]
    private bool _isInLibrary;

    [ObservableProperty]
    private Bitmap? _coverImage;

    public CatalogSearchCardViewModel(JikanSearchResult result, MediaType type, bool isInLibrary = false)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Type = type;
        _isInLibrary = isInLibrary;
        _coverImage = result.CoverImage;
    }

    public void LoadCoverImage()
    {
        _ = LoadCoverImageAsync();
    }

    public async Task LoadCoverImageAsync()
    {
        if (CoverImage == null && !string.IsNullOrWhiteSpace(CoverImageUrl))
        {
            var bmp = await ImageLoader.LoadFromUrlAsync(CoverImageUrl);
            if (bmp != null)
            {
                CoverImage = bmp;
            }
        }
    }
}
