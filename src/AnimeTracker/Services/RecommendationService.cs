using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Models;
using AnimeTracker.Services.Jikan;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Services;

public class RecommendationItem
{
    public MediaItem Item { get; }
    public double MatchScore { get; }
    public IReadOnlyList<string> MatchingGenres { get; }
    public string Reason { get; }
    public double? ExternalScore { get; }
    public bool IsExternal => Item.Id == 0;

    public RecommendationItem(
        MediaItem item,
        double matchScore,
        IReadOnlyList<string> matchingGenres,
        string reason,
        double? externalScore = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        MatchScore = matchScore;
        MatchingGenres = matchingGenres ?? Array.Empty<string>();
        Reason = reason ?? string.Empty;
        ExternalScore = externalScore;
    }
}

public class RecommendationResult
{
    public IReadOnlyList<RecommendationItem> Recommendations { get; }
    public IReadOnlyList<string> TopFavoriteGenres { get; }
    public string? EmptyStateMessage { get; }
    public string? ErrorMessage { get; }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasRecommendations => Recommendations.Count > 0;

    public RecommendationResult(
        IReadOnlyList<RecommendationItem> recommendations,
        IReadOnlyList<string> topFavoriteGenres,
        string? emptyStateMessage = null,
        string? errorMessage = null)
    {
        Recommendations = recommendations ?? Array.Empty<RecommendationItem>();
        TopFavoriteGenres = topFavoriteGenres ?? Array.Empty<string>();
        EmptyStateMessage = emptyStateMessage;
        ErrorMessage = errorMessage;
    }
}

public class UserTasteProfile
{
    public MediaTypeFilter MediaFilter { get; }
    public IReadOnlyDictionary<string, double> GenreWeights { get; }
    public IReadOnlyList<string> TopGenres { get; }
    public int FavoriteItemCount { get; }
    public bool HasPreferences => FavoriteItemCount > 0 && TopGenres.Count > 0;

    public UserTasteProfile(
        MediaTypeFilter mediaFilter,
        IReadOnlyDictionary<string, double> genreWeights,
        IReadOnlyList<string> topGenres,
        int favoriteItemCount)
    {
        MediaFilter = mediaFilter;
        GenreWeights = genreWeights ?? new Dictionary<string, double>();
        TopGenres = topGenres ?? Array.Empty<string>();
        FavoriteItemCount = favoriteItemCount;
    }
}

public class RecommendationService
{
    private readonly JikanApiClient _jikanClient;

    public RecommendationService(JikanApiClient? jikanClient = null)
    {
        _jikanClient = jikanClient ?? new JikanApiClient();
    }

    public UserTasteProfile BuildTasteProfile(
        IReadOnlyList<MediaItem> allItems,
        MediaTypeFilter mediaFilter = MediaTypeFilter.All)
    {
        ArgumentNullException.ThrowIfNull(allItems);

        IEnumerable<MediaItem> scoped = allItems;
        scoped = mediaFilter switch
        {
            MediaTypeFilter.Anime => scoped.Where(x => x.Type == MediaType.Anime),
            MediaTypeFilter.Manga => scoped.Where(x => x.Type == MediaType.Manga),
            MediaTypeFilter.Manhwa => scoped.Where(x => x.Type == MediaType.Manhwa),
            _ => scoped
        };

        var favorites = scoped
            .Where(x => x.Score.HasValue && x.Score.Value >= 8)
            .ToList();

        if (favorites.Count == 0)
        {
            return new UserTasteProfile(
                mediaFilter,
                new Dictionary<string, double>(),
                Array.Empty<string>(),
                0);
        }

        var genreWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genreDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fav in favorites)
        {
            if (string.IsNullOrWhiteSpace(fav.Genres)) continue;

            var genres = ParseGenres(fav.Genres);
            foreach (var g in genres)
            {
                var weight = fav.Score!.Value;
                genreWeights[g] = genreWeights.GetValueOrDefault(g) + weight;
                if (!genreDisplayNames.ContainsKey(g))
                {
                    genreDisplayNames[g] = CapitalizeWords(g);
                }
            }
        }

        var topGenres = genreWeights
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => genreDisplayNames[kv.Key])
            .ToList();

        return new UserTasteProfile(mediaFilter, genreWeights, topGenres, favorites.Count);
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var chars = title
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray();
        var cleaned = new string(chars).Trim();
        return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
    }

    public IReadOnlyList<RecommendationItem> RankCandidates(
        UserTasteProfile tasteProfile,
        IReadOnlyList<JikanSearchResult> candidates,
        IReadOnlyList<MediaItem> existingItems,
        string? selectedGenre = null,
        int maxResults = 25)
    {
        ArgumentNullException.ThrowIfNull(tasteProfile);
        ArgumentNullException.ThrowIfNull(candidates);
        existingItems ??= Array.Empty<MediaItem>();

        // Build exclusion sets from existing library
        var existingMalKeys = new HashSet<(int MalId, string MalType)>();
        var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in existingItems)
        {
            if (item.MalId.HasValue && !string.IsNullOrWhiteSpace(item.MalType))
            {
                existingMalKeys.Add((item.MalId.Value, item.MalType.Trim().ToLowerInvariant()));
            }
            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                existingTitles.Add(NormalizeTitle(item.Title));
            }
        }

        var seenMalKeys = new HashSet<(int, string)>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eligibleCandidates = new List<JikanSearchResult>();

        var specificGenre = (!string.IsNullOrWhiteSpace(selectedGenre) && !string.Equals(selectedGenre, "All Genres", StringComparison.OrdinalIgnoreCase))
            ? selectedGenre.Trim()
            : null;

        foreach (var c in candidates)
        {
            if (c.MalId <= 0 && string.IsNullOrWhiteSpace(c.Title)) continue;

            // 1. Stable identifier check against existing library and prior candidates
            if (c.MalId > 0 && !string.IsNullOrWhiteSpace(c.MalType))
            {
                var key = (c.MalId, c.MalType.Trim().ToLowerInvariant());
                if (existingMalKeys.Contains(key)) continue;
                if (!seenMalKeys.Add(key)) continue;
            }

            // 2. Normalized title check against existing library and prior candidates
            var normTitle = NormalizeTitle(c.Title);
            var normEng = NormalizeTitle(c.EnglishTitle);

            if (!string.IsNullOrEmpty(normTitle))
            {
                if (existingTitles.Contains(normTitle)) continue;
                if (!seenTitles.Add(normTitle)) continue;
            }
            if (!string.IsNullOrEmpty(normEng))
            {
                if (existingTitles.Contains(normEng)) continue;
                seenTitles.Add(normEng);
            }

            // 3. Optional genre filter check
            if (specificGenre != null)
            {
                var hasGenre = c.Genres.Any(g => string.Equals(g.Trim(), specificGenre, StringComparison.OrdinalIgnoreCase));
                if (!hasGenre) continue;
            }

            eligibleCandidates.Add(c);
        }

        // Rank candidates using deterministic rule-based scoring:
        // - Genre affinity: Sum of weights of matching genres from the user's high-rated titles
        // - Community score: Modest bonus factor (1.5)
        // - Diversity factor: Slight discount for duplicate primary genres so one category does not dominate
        var primaryGenreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var scoredItems = new List<RecommendationItem>();

        foreach (var c in eligibleCandidates)
        {
            var matching = new List<string>();
            double genreAffinity = 0;

            foreach (var g in c.Genres)
            {
                if (tasteProfile.GenreWeights.TryGetValue(g, out var weight))
                {
                    genreAffinity += weight;
                    matching.Add(CapitalizeWords(g));
                }
            }

            double publicScore = c.Score ?? 6.0;
            double scoreBonus = publicScore * 1.5;

            string primaryGenre = c.Genres.FirstOrDefault() ?? "General";
            int occurrence = primaryGenreCounts.GetValueOrDefault(primaryGenre, 0);
            primaryGenreCounts[primaryGenre] = occurrence + 1;
            double diversityFactor = 1.0 / (1.0 + (0.08 * occurrence));

            double finalScore = Math.Round((genreAffinity * 2.0 * diversityFactor) + scoreBonus, 1);

            string reason;
            if (matching.Count > 0)
            {
                reason = $"Matches your highly rated genres: {string.Join(", ", matching.Take(3))}";
            }
            else
            {
                reason = $"Top rated in {CapitalizeWords(specificGenre ?? primaryGenre)} matching your preferences";
            }

            var isAnime = string.Equals(c.MalType, "anime", StringComparison.OrdinalIgnoreCase);
            var isManhwa = string.Equals(c.MalType, "manhwa", StringComparison.OrdinalIgnoreCase)
                           || (tasteProfile.MediaFilter == MediaTypeFilter.Manhwa && !isAnime);

            var mediaItem = new MediaItem
            {
                Id = 0,
                MalId = c.MalId > 0 ? c.MalId : null,
                MalType = !string.IsNullOrWhiteSpace(c.MalType) ? c.MalType : null,
                Title = !string.IsNullOrWhiteSpace(c.EnglishTitle) ? c.EnglishTitle : c.Title,
                Type = isAnime
                    ? MediaType.Anime
                    : (isManhwa ? MediaType.Manhwa : MediaType.Manga),
                Status = isAnime
                    ? MediaStatus.PlanToWatch
                    : MediaStatus.PlanToRead,
                CurrentProgress = 0,
                TotalProgress = c.TotalCount,
                CoverImageUrl = c.CoverImageUrl,
                Genres = c.Genres.Count > 0 ? string.Join(", ", c.Genres) : null,
                Synopsis = c.Synopsis,
                ReleaseYear = c.Year,
                Score = null,
                RewatchCount = 0
            };

            scoredItems.Add(new RecommendationItem(mediaItem, finalScore, matching, reason, c.Score));
        }

        return scoredItems
            .OrderByDescending(x => x.MatchScore)
            .ThenByDescending(x => x.ExternalScore ?? 0)
            .ThenBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public async Task<RecommendationResult> GetExternalRecommendationsAsync(
        IReadOnlyList<MediaItem> allItems,
        MediaTypeFilter typeFilter = MediaTypeFilter.All,
        string? selectedGenre = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allItems);
        cancellationToken.ThrowIfCancellationRequested();

        var tasteProfile = BuildTasteProfile(allItems, typeFilter);
        if (!tasteProfile.HasPreferences)
        {
            var guidanceMessage = typeFilter switch
            {
                MediaTypeFilter.Anime => "Rate some anime 8/10 or higher to discover personalized recommendations.",
                MediaTypeFilter.Manga => "Rate some manga 8/10 or higher to discover personalized recommendations.",
                MediaTypeFilter.Manhwa => "Rate some manhwa 8/10 or higher to discover personalized recommendations.",
                _ => "Rate some titles 8/10 or higher to discover personalized recommendations."
            };

            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                Array.Empty<string>(),
                emptyStateMessage: guidanceMessage);
        }

        // Determine which genres to fetch
        var genresToFetch = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectedGenre) && !string.Equals(selectedGenre, "All Genres", StringComparison.OrdinalIgnoreCase))
        {
            genresToFetch.Add(selectedGenre.Trim());
        }
        else
        {
            // Fetch top 1-2 genres from taste profile
            genresToFetch.AddRange(tasteProfile.TopGenres.Take(2));
        }

        var typesToFetch = typeFilter switch
        {
            MediaTypeFilter.Anime => new[] { "anime" },
            MediaTypeFilter.Manga => new[] { "manga" },
            MediaTypeFilter.Manhwa => new[] { "manhwa" },
            _ => new[] { "anime", "manga", "manhwa" }
        };

        var rawCandidates = new List<JikanSearchResult>();

        try
        {
            foreach (var genre in genresToFetch)
            {
                foreach (var malType in typesToFetch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var results = await _jikanClient.GetRecommendationsByGenreAsync(malType, genre, cancellationToken);
                    rawCandidates.AddRange(results);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JikanApiException ex) when (ex.IsRateLimited)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                tasteProfile.TopGenres,
                errorMessage: "Catalog rate limit reached (HTTP 429). Please wait a moment before refreshing.");
        }
        catch (JikanApiException)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                tasteProfile.TopGenres,
                errorMessage: "Unable to retrieve recommendations from the catalog. Please try again later.");
        }
        catch (Exception ex)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                tasteProfile.TopGenres,
                errorMessage: $"Error loading recommendations: {ex.Message}");
        }

        var ranked = RankCandidates(tasteProfile, rawCandidates, allItems, selectedGenre, maxResults: 30);

        if (ranked.Count == 0)
        {
            return new RecommendationResult(
                Array.Empty<RecommendationItem>(),
                tasteProfile.TopGenres,
                emptyStateMessage: "No external titles found matching your favorite genres.");
        }

        return new RecommendationResult(ranked, tasteProfile.TopGenres);
    }

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
