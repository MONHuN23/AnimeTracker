using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnimeTracker.Models;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Services;

public class RecommendationItem
{
    public MediaItem Item { get; }
    public double MatchScore { get; }
    public IReadOnlyList<string> MatchingGenres { get; }
    public string Reason { get; }

    public RecommendationItem(
        MediaItem item,
        double matchScore,
        IReadOnlyList<string> matchingGenres,
        string reason)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        MatchScore = matchScore;
        MatchingGenres = matchingGenres ?? Array.Empty<string>();
        Reason = reason ?? string.Empty;
    }
}

public class RecommendationResult
{
    public IReadOnlyList<RecommendationItem> Recommendations { get; }
    public IReadOnlyList<string> TopFavoriteGenres { get; }
    public string? EmptyStateMessage { get; }
    public bool HasRecommendations => Recommendations.Count > 0;

    public RecommendationResult(
        IReadOnlyList<RecommendationItem> recommendations,
        IReadOnlyList<string> topFavoriteGenres,
        string? emptyStateMessage = null)
    {
        Recommendations = recommendations ?? Array.Empty<RecommendationItem>();
        TopFavoriteGenres = topFavoriteGenres ?? Array.Empty<string>();
        EmptyStateMessage = emptyStateMessage;
    }
}

public class RecommendationService
{
    public RecommendationResult GetRecommendations(
        IReadOnlyList<MediaItem> allItems,
        MediaTypeFilter typeFilter = MediaTypeFilter.All,
        string? selectedGenre = null)
    {
        ArgumentNullException.ThrowIfNull(allItems);

        // 1. Identify favorite items (Score >= 8)
        var favoriteItems = allItems
            .Where(x => x.Score.HasValue && x.Score.Value >= 8)
            .ToList();

        if (favoriteItems.Count == 0)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                Array.Empty<string>(),
                "Rate some titles 8/10 or higher to discover personalized recommendations.");
        }

        // 2. Tally favorite genres and their weights
        var genreWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genreDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fav in favoriteItems)
        {
            if (string.IsNullOrWhiteSpace(fav.Genres)) continue;

            var genres = ParseGenres(fav.Genres);
            foreach (var genre in genres)
            {
                var weight = fav.Score!.Value;
                genreWeights[genre] = genreWeights.GetValueOrDefault(genre) + weight;

                if (!genreDisplayNames.ContainsKey(genre))
                {
                    genreDisplayNames[genre] = CapitalizeWords(genre);
                }
            }
        }

        var topFavoriteGenres = genreWeights
            .OrderByDescending(kv => kv.Value)
            .Select(kv => genreDisplayNames[kv.Key])
            .ToList();

        if (genreWeights.Count == 0)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                Array.Empty<string>(),
                "Your highly rated titles do not have genre information.");
        }

        // 3. Find candidate items in backlog (ONLY PlanToWatch / PlanToRead)
        var backlogCandidates = allItems
            .Where(x => x.Status == MediaStatus.PlanToWatch || x.Status == MediaStatus.PlanToRead)
            .ToList();

        // Apply media type filter
        backlogCandidates = typeFilter switch
        {
            MediaTypeFilter.Anime => backlogCandidates.Where(x => x.Type == MediaType.Anime).ToList(),
            MediaTypeFilter.Manga => backlogCandidates.Where(x => x.Type == MediaType.Manga).ToList(),
            MediaTypeFilter.Manhwa => backlogCandidates.Where(x => x.Type == MediaType.Manhwa).ToList(),
            _ => backlogCandidates
        };

        if (backlogCandidates.Count == 0)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                topFavoriteGenres,
                "Your backlog is empty. Add titles to Plan to Watch / Read to see recommendations.");
        }

        // 4. Score each candidate based on genre overlap
        var recommendations = new List<RecommendationItem>();

        foreach (var candidate in backlogCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Genres)) continue;

            var candidateGenres = ParseGenres(candidate.Genres);

            // Optional filter by specific genre
            if (!string.IsNullOrWhiteSpace(selectedGenre))
            {
                if (!candidateGenres.Any(g => string.Equals(g, selectedGenre.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            var matchingGenres = new List<string>();
            double score = 0;

            foreach (var g in candidateGenres)
            {
                if (genreWeights.TryGetValue(g, out var weight))
                {
                    score += weight;
                    matchingGenres.Add(genreDisplayNames.GetValueOrDefault(g, g));
                }
            }

            if (score > 0 && matchingGenres.Count > 0)
            {
                var genreSummary = string.Join(", ", matchingGenres);
                var reason = $"Matches your highly rated genres: {genreSummary}";
                recommendations.Add(new RecommendationItem(candidate, score, matchingGenres, reason));
            }
        }

        // 5. Rank by score descending, then title alphabetically
        var ranked = recommendations
            .OrderByDescending(r => r.MatchScore)
            .ThenBy(r => r.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ranked.Count == 0)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                topFavoriteGenres,
                "No backlog titles match your favorite genres yet.");
        }

        return new RecommendationResult(ranked, topFavoriteGenres);
    }

    private static List<string> ParseGenres(string genresString)
    {
        return genresString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string CapitalizeWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(input.ToLowerInvariant());
    }
}
