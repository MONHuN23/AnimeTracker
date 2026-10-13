using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnimeTracker.Services.Jikan;

public partial class JikanSearchResult : ObservableObject
{
    public int MalId { get; init; }
    public string MalType { get; init; } = string.Empty; // "anime" or "manga"
    public string Title { get; init; } = string.Empty;
    public string? EnglishTitle { get; init; }
    public string? CoverImageUrl { get; init; }
    public int? TotalCount { get; init; } // episodes for anime, chapters for manga
    public string? Synopsis { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public double? Score { get; init; }
    public int? Year { get; init; }

    [ObservableProperty]
    private Bitmap? _coverImage;

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
