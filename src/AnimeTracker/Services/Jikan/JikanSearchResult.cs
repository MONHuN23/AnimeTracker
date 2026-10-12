using System;
using System.Collections.Generic;

namespace AnimeTracker.Services.Jikan;

public class JikanSearchResult
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
}
