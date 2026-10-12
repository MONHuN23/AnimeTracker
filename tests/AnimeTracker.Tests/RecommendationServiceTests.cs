using System;
using System.Collections.Generic;
using System.Linq;
using AnimeTracker.Models;
using AnimeTracker.Services;
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
}
