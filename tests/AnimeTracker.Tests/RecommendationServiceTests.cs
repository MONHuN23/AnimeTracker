using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Jikan;
using AnimeTracker.ViewModels;
using Xunit;

namespace AnimeTracker.Tests;

public class RecommendationServiceTests
{
    private readonly RecommendationService _service = new();

    [Fact]
    public void GetRecommendations_ReturnsEmpty_WhenNoItemsRatedEightOrHigher()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Show 1", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 7, Genres = "Action, Fantasy" },
            new() { Title = "Show 2", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Action" }
        };

        var result = _service.GetRecommendations(items);

        Assert.False(result.HasRecommendations);
        Assert.Empty(result.Recommendations);
        Assert.NotNull(result.EmptyStateMessage);
        Assert.Contains("8", result.EmptyStateMessage);
    }

    [Fact]
    public void GetRecommendations_ReturnsEmpty_WhenBacklogIsEmpty()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Rated 10", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 10, Genres = "Fantasy" },
            new() { Title = "Watching", Type = MediaType.Anime, Status = MediaStatus.Watching, Genres = "Fantasy" }
        };

        var result = _service.GetRecommendations(items);

        Assert.False(result.HasRecommendations);
        Assert.Empty(result.Recommendations);
        Assert.NotNull(result.EmptyStateMessage);
        Assert.Contains("backlog", result.EmptyStateMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetRecommendations_ExcludesStartedCompletedOnHoldAndDroppedItems()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Favorite", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 9, Genres = "Sci-Fi" },
            new() { Title = "Active", Type = MediaType.Anime, Status = MediaStatus.Watching, Genres = "Sci-Fi" },
            new() { Title = "Finished", Type = MediaType.Anime, Status = MediaStatus.Completed, Genres = "Sci-Fi" },
            new() { Title = "Paused", Type = MediaType.Anime, Status = MediaStatus.OnHold, Genres = "Sci-Fi" },
            new() { Title = "Abandoned", Type = MediaType.Anime, Status = MediaStatus.Dropped, Genres = "Sci-Fi" },
            new() { Title = "Candidate", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Sci-Fi" }
        };

        var result = _service.GetRecommendations(items);

        Assert.True(result.HasRecommendations);
        Assert.Single(result.Recommendations);
        Assert.Equal("Candidate", result.Recommendations[0].Item.Title);
    }

    [Fact]
    public void GetRecommendations_RanksByMatchingScoreDescending()
    {
        var items = new List<MediaItem>
        {
            // Favorite items: Fantasy, Action, Drama
            new() { Title = "Fav 1", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 10, Genres = "Action, Fantasy" },
            new() { Title = "Fav 2", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 8, Genres = "Fantasy, Drama" },

            // Candidates
            // Candidate A matches Fantasy (10+8=18) and Action (10) -> score 28
            new() { Title = "Candidate A", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Action, Fantasy" },
            // Candidate B matches Drama (8) -> score 8
            new() { Title = "Candidate B", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Drama" },
            // Candidate C matches Comedy (not in favorites) -> score 0 (not recommended)
            new() { Title = "Candidate C", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Comedy" }
        };

        var result = _service.GetRecommendations(items);

        Assert.Equal(2, result.Recommendations.Count);
        Assert.Equal("Candidate A", result.Recommendations[0].Item.Title);
        Assert.Equal("Candidate B", result.Recommendations[1].Item.Title);
        Assert.True(result.Recommendations[0].MatchScore > result.Recommendations[1].MatchScore);
    }

    [Fact]
    public void GetRecommendations_TieBreaking_SortsAlphabeticallyByTitle()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Fav", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 9, Genres = "Mystery" },
            new() { Title = "Zulu Mystery", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Mystery" },
            new() { Title = "Alpha Mystery", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Mystery" }
        };

        var result = _service.GetRecommendations(items);

        Assert.Equal(2, result.Recommendations.Count);
        Assert.Equal("Alpha Mystery", result.Recommendations[0].Item.Title);
        Assert.Equal("Zulu Mystery", result.Recommendations[1].Item.Title);
    }

    [Fact]
    public void GetRecommendations_GeneratesFactualReason()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Frieren", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 10, Genres = "Adventure, Fantasy" },
            new() { Title = "Dungeon Meshi", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Fantasy, Comedy" }
        };

        var result = _service.GetRecommendations(items);

        Assert.Single(result.Recommendations);
        var rec = result.Recommendations[0];
        Assert.Contains("Fantasy", rec.Reason);
        Assert.Contains("Fantasy", rec.MatchingGenres);
    }

    [Fact]
    public void GetRecommendations_FiltersByMediaType()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Fav Anime", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 9, Genres = "Cyberpunk" },
            new() { Title = "Anime Backlog", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Cyberpunk" },
            new() { Title = "Manga Backlog", Type = MediaType.Manga, Status = MediaStatus.PlanToRead, Genres = "Cyberpunk" }
        };

        var allRecs = _service.GetRecommendations(items, MediaTypeFilter.All);
        var animeOnly = _service.GetRecommendations(items, MediaTypeFilter.Anime);
        var mangaOnly = _service.GetRecommendations(items, MediaTypeFilter.Manga);

        Assert.Equal(2, allRecs.Recommendations.Count);
        Assert.Single(animeOnly.Recommendations);
        Assert.Equal("Anime Backlog", animeOnly.Recommendations[0].Item.Title);
        Assert.Single(mangaOnly.Recommendations);
        Assert.Equal("Manga Backlog", mangaOnly.Recommendations[0].Item.Title);
    }

    [Fact]
    public void GetRecommendations_FiltersBySpecificGenre_WhenProvided()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Fav 1", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 9, Genres = "Horror, Mystery" },
            new() { Title = "Candidate Horror", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Horror" },
            new() { Title = "Candidate Mystery", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "Mystery" }
        };

        var horrorResult = _service.GetRecommendations(items, MediaTypeFilter.All, selectedGenre: "Horror");

        Assert.Single(horrorResult.Recommendations);
        Assert.Equal("Candidate Horror", horrorResult.Recommendations[0].Item.Title);
    }

    [Fact]
    public void GetRecommendations_CaseInsensitiveGenreMatching_AndHandlesWhitespace()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Fav", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 8, Genres = "  SCI-FI ,  Mecha  " },
            new() { Title = "Candidate", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = "sci-fi, space" }
        };

        var result = _service.GetRecommendations(items);

        Assert.Single(result.Recommendations);
        Assert.Contains("Sci-Fi", result.Recommendations[0].MatchingGenres, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetRecommendations_HandlesMissingOrEmptyGenres_WithoutThrowing()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Fav No Genre", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 9, Genres = null },
            new() { Title = "Fav Empty", Type = MediaType.Anime, Status = MediaStatus.Completed, Score = 9, Genres = "   " },
            new() { Title = "Backlog No Genre", Type = MediaType.Anime, Status = MediaStatus.PlanToWatch, Genres = null }
        };

        var result = _service.GetRecommendations(items);

        Assert.False(result.HasRecommendations);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void BuildTasteProfile_IncludesOnlyItemsRatedEightOrHigher()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Rated 7", Type = MediaType.Anime, Score = 7, Genres = "Action" },
            new() { Title = "Rated 8", Type = MediaType.Anime, Score = 8, Genres = "Fantasy" },
            new() { Title = "Rated 10", Type = MediaType.Anime, Score = 10, Genres = "Fantasy, Adventure" },
            new() { Title = "Unrated", Type = MediaType.Anime, Score = null, Genres = "Comedy" }
        };

        var profile = _service.BuildTasteProfile(items, MediaTypeFilter.All);

        Assert.True(profile.HasPreferences);
        Assert.Equal(2, profile.FavoriteItemCount);
        Assert.False(profile.GenreWeights.ContainsKey("Action"));
        Assert.False(profile.GenreWeights.ContainsKey("Comedy"));
        Assert.True(profile.GenreWeights.ContainsKey("Fantasy"));
        Assert.True(profile.GenreWeights.ContainsKey("Adventure"));
        Assert.Equal(18, profile.GenreWeights["Fantasy"]); // 8 + 10
        Assert.Equal(10, profile.GenreWeights["Adventure"]);
        Assert.Equal("Fantasy", profile.TopGenres[0]);
    }

    [Fact]
    public void BuildTasteProfile_DistinguishesAnimeMangaAndManhwaFilters()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Anime Fav", Type = MediaType.Anime, Score = 10, Genres = "Cyberpunk" },
            new() { Title = "Manga Fav", Type = MediaType.Manga, Score = 9, Genres = "Horror" },
            new() { Title = "Manhwa Fav", Type = MediaType.Manhwa, Score = 9, Genres = "Martial Arts" }
        };

        var animeProfile = _service.BuildTasteProfile(items, MediaTypeFilter.Anime);
        var mangaProfile = _service.BuildTasteProfile(items, MediaTypeFilter.Manga);
        var manhwaProfile = _service.BuildTasteProfile(items, MediaTypeFilter.Manhwa);
        var allProfile = _service.BuildTasteProfile(items, MediaTypeFilter.All);

        Assert.Single(animeProfile.TopGenres);
        Assert.Equal("Cyberpunk", animeProfile.TopGenres[0]);

        Assert.Single(mangaProfile.TopGenres);
        Assert.Equal("Horror", mangaProfile.TopGenres[0]);

        Assert.Single(manhwaProfile.TopGenres);
        Assert.Equal("Martial Arts", manhwaProfile.TopGenres[0]);

        Assert.Equal(3, allProfile.TopGenres.Count);
        Assert.Contains("Cyberpunk", allProfile.TopGenres);
        Assert.Contains("Horror", allProfile.TopGenres);
        Assert.Contains("Martial Arts", allProfile.TopGenres);
    }

    [Fact]
    public void BuildTasteProfile_ReturnsEmpty_WhenNoItemsRatedEightOrHigher()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Item 1", Type = MediaType.Anime, Score = 6, Genres = "Action" },
            new() { Title = "Item 2", Type = MediaType.Anime, Score = null, Genres = "Drama" }
        };

        var profile = _service.BuildTasteProfile(items, MediaTypeFilter.All);

        Assert.False(profile.HasPreferences);
        Assert.Empty(profile.TopGenres);
        Assert.Empty(profile.GenreWeights);
        Assert.Equal(0, profile.FavoriteItemCount);
    }

    [Fact]
    public void RankCandidates_ExcludesTitlesAlreadyInLibrary_ByMalId()
    {
        var tasteProfile = new UserTasteProfile(
            MediaTypeFilter.All,
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Action"] = 10 },
            new[] { "Action" },
            1);

        var existingInLibrary = new List<MediaItem>
        {
            new() { MalId = 101, MalType = "anime", Title = "Existing Anime", Type = MediaType.Anime }
        };

        var candidates = new List<JikanSearchResult>
        {
            new() { MalId = 101, MalType = "anime", Title = "Existing Anime (External)", Genres = new[] { "Action" }, Score = 8.5 },
            new() { MalId = 102, MalType = "anime", Title = "Fresh Candidate", Genres = new[] { "Action" }, Score = 8.0 }
        };

        var ranked = _service.RankCandidates(tasteProfile, candidates, existingInLibrary);

        Assert.Single(ranked);
        Assert.Equal(102, ranked[0].Item.MalId);
        Assert.Equal("Fresh Candidate", ranked[0].Item.Title);
    }

    [Fact]
    public void RankCandidates_ExcludesTitlesAlreadyInLibrary_ByNormalizedTitle()
    {
        var tasteProfile = new UserTasteProfile(
            MediaTypeFilter.All,
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Action"] = 10 },
            new[] { "Action" },
            1);

        var existingInLibrary = new List<MediaItem>
        {
            new() { Title = "Attack on Titan: Final Season", Type = MediaType.Anime }
        };

        var candidates = new List<JikanSearchResult>
        {
            new() { MalId = 500, MalType = "anime", Title = "Attack on Titan Final Season!", Genres = new[] { "Action" }, Score = 9.0 },
            new() { MalId = 501, MalType = "anime", Title = "Vinland Saga", Genres = new[] { "Action" }, Score = 8.8 }
        };

        var ranked = _service.RankCandidates(tasteProfile, candidates, existingInLibrary);

        Assert.Single(ranked);
        Assert.Equal("Vinland Saga", ranked[0].Item.Title);
    }

    [Fact]
    public void RankCandidates_DeduplicatesCandidatesAmongThemselves()
    {
        var tasteProfile = new UserTasteProfile(
            MediaTypeFilter.All,
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Action"] = 10 },
            new[] { "Action" },
            1);

        var candidates = new List<JikanSearchResult>
        {
            new() { MalId = 201, MalType = "anime", Title = "Duplicate Show", Genres = new[] { "Action" }, Score = 8.0 },
            new() { MalId = 201, MalType = "anime", Title = "Duplicate Show Copy", Genres = new[] { "Action" }, Score = 8.0 },
            new() { MalId = 202, MalType = "anime", Title = "Duplicate Show", Genres = new[] { "Action" }, Score = 7.5 }
        };

        var ranked = _service.RankCandidates(tasteProfile, candidates, Array.Empty<MediaItem>());

        Assert.Single(ranked);
        Assert.Equal(201, ranked[0].Item.MalId);
    }

    [Fact]
    public void RankCandidates_RanksByGenreAffinity_AndScoresCorrectly()
    {
        var genreWeights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"] = 20,
            ["Fantasy"] = 10,
            ["Comedy"] = 5
        };
        var tasteProfile = new UserTasteProfile(MediaTypeFilter.All, genreWeights, new[] { "Action", "Fantasy", "Comedy" }, 3);

        var candidates = new List<JikanSearchResult>
        {
            // Candidate High: Action + Fantasy (20+10 = 30 affinity)
            new() { MalId = 301, MalType = "anime", Title = "Candidate High", Genres = new[] { "Action", "Fantasy" }, Score = 8.0 },
            // Candidate Low: Comedy only (5 affinity), higher public score 9.5
            new() { MalId = 302, MalType = "anime", Title = "Candidate Low", Genres = new[] { "Comedy" }, Score = 9.5 }
        };

        var ranked = _service.RankCandidates(tasteProfile, candidates, Array.Empty<MediaItem>());

        Assert.Equal(2, ranked.Count);
        Assert.Equal("Candidate High", ranked[0].Item.Title);
        Assert.Equal("Candidate Low", ranked[1].Item.Title);
        Assert.True(ranked[0].MatchScore > ranked[1].MatchScore);
        Assert.Contains("Action", ranked[0].Reason);
    }

    [Fact]
    public async Task GetExternalRecommendationsAsync_WhenNoItemsRatedEightOrHigher_ReturnsGuidanceMessage()
    {
        var items = new List<MediaItem>
        {
            new() { Title = "Average", Type = MediaType.Anime, Score = 6, Genres = "Action" }
        };

        var result = await _service.GetExternalRecommendationsAsync(items, MediaTypeFilter.Anime);

        Assert.False(result.HasRecommendations);
        Assert.Empty(result.Recommendations);
        Assert.NotNull(result.EmptyStateMessage);
        Assert.Contains("8/10", result.EmptyStateMessage);
    }

    [Fact]
    public async Task GetExternalRecommendationsAsync_HandlesApiRateLimitOrError_Gracefully()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var service = new RecommendationService(jikanClient);

        var items = new List<MediaItem>
        {
            new() { Title = "Top Show", Type = MediaType.Anime, Score = 10, Genres = "Action" }
        };

        var result = await service.GetExternalRecommendationsAsync(items, MediaTypeFilter.Anime);

        Assert.False(result.HasRecommendations);
        Assert.True(result.HasError);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("rate limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetExternalRecommendationsAsync_SeparatesMangaAndManhwaQueries()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var jikanClient = new JikanApiClient(httpClient);
        var service = new RecommendationService(jikanClient);

        var mangaItems = new List<MediaItem>
        {
            new() { Title = "Berserk", Type = MediaType.Manga, Score = 10, Genres = "Dark Fantasy" }
        };
        var manhwaItems = new List<MediaItem>
        {
            new() { Title = "Solo Leveling", Type = MediaType.Manhwa, Score = 10, Genres = "Action" }
        };

        await service.GetExternalRecommendationsAsync(mangaItems, MediaTypeFilter.Manga);
        var mangaRequestUri = handler.SentRequests.LastOrDefault()?.RequestUri?.ToString();

        await service.GetExternalRecommendationsAsync(manhwaItems, MediaTypeFilter.Manhwa);
        var manhwaRequestUri = handler.SentRequests.LastOrDefault()?.RequestUri?.ToString();

        Assert.NotNull(mangaRequestUri);
        Assert.NotNull(manhwaRequestUri);
        Assert.Contains("type=manga", mangaRequestUri);
        Assert.Contains("type=manhwa", manhwaRequestUri);
    }

    [Fact]
    public void RankCandidates_AssignsCorrectMediaType_ForMangaAndManhwa()
    {
        var tasteProfile = new UserTasteProfile(
            MediaTypeFilter.All,
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Action"] = 10 },
            new[] { "Action" },
            1);

        var candidates = new List<JikanSearchResult>
        {
            new() { MalId = 1, MalType = "manhwa", Title = "Solo Leveling", Genres = new[] { "Action" }, Score = 8.8 },
            new() { MalId = 2, MalType = "manga", Title = "Chainsaw Man", Genres = new[] { "Action" }, Score = 8.7 },
            new() { MalId = 3, MalType = "anime", Title = "Demon Slayer", Genres = new[] { "Action" }, Score = 8.6 }
        };

        var ranked = _service.RankCandidates(tasteProfile, candidates, Array.Empty<MediaItem>());

        Assert.Equal(3, ranked.Count);
        var manhwaRec = ranked.Single(x => x.Item.Title == "Solo Leveling");
        var mangaRec = ranked.Single(x => x.Item.Title == "Chainsaw Man");
        var animeRec = ranked.Single(x => x.Item.Title == "Demon Slayer");

        Assert.Equal(MediaType.Manhwa, manhwaRec.Item.Type);
        Assert.Equal(MediaType.Manga, mangaRec.Item.Type);
        Assert.Equal(MediaType.Anime, animeRec.Item.Type);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> SentRequests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        }
    }
}
