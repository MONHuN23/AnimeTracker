using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class MediaTrackingServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;
    private readonly MediaTrackingService _service;

    public MediaTrackingServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_tracking_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _repository = new MediaItemRepository(_connectionString);
        _service = new MediaTrackingService(_repository);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try
            {
                SqliteConnection.ClearAllPools();
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public async Task IncrementProgressAsync_IncreasesProgressByOne()
    {
        var item = new MediaItem
        {
            Title = "Attack on Titan",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 5,
            TotalProgress = 25
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.IncrementProgressAsync(id);

        Assert.Equal(6, result.CurrentProgress);
    }

    [Fact]
    public async Task DecrementProgressAsync_DecreasesProgressByOne()
    {
        var item = new MediaItem
        {
            Title = "Attack on Titan",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 5,
            TotalProgress = 25
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.DecrementProgressAsync(id);

        Assert.Equal(4, result.CurrentProgress);
    }

    [Fact]
    public async Task DecrementProgressAsync_DoesNotDecreaseBelowZero()
    {
        var item = new MediaItem
        {
            Title = "New Series",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 0,
            TotalProgress = 12
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.DecrementProgressAsync(id);

        Assert.Equal(0, result.CurrentProgress);
    }

    [Fact]
    public async Task IncrementProgressAsync_DoesNotExceedKnownTotalProgress()
    {
        var item = new MediaItem
        {
            Title = "Completed Length Series",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 12,
            TotalProgress = 12
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.IncrementProgressAsync(id);

        Assert.Equal(12, result.CurrentProgress);
    }

    [Fact]
    public async Task IncrementProgressAsync_WithoutKnownTotal_IncreasesFreely()
    {
        var item = new MediaItem
        {
            Title = "One Piece",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 1100,
            TotalProgress = null
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.IncrementProgressAsync(id);

        Assert.Equal(1101, result.CurrentProgress);
    }

    [Fact]
    public async Task CompleteAsync_SetsStatusToCompleted_AndAlignsProgressWhenTotalIsKnown()
    {
        var item = new MediaItem
        {
            Title = "Steins;Gate",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 20,
            TotalProgress = 24
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.CompleteAsync(id);

        Assert.Equal(MediaStatus.Completed, result.Status);
        Assert.Equal(24, result.CurrentProgress);
    }

    [Fact]
    public async Task CompleteAsync_SetsStatusToCompleted_AndPreservesProgressWhenTotalIsUnknown()
    {
        var item = new MediaItem
        {
            Title = "Ongoing Manga",
            Type = MediaType.Manga,
            Status = MediaStatus.Reading,
            CurrentProgress = 85,
            TotalProgress = null
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.CompleteAsync(id);

        Assert.Equal(MediaStatus.Completed, result.Status);
        Assert.Equal(85, result.CurrentProgress);
    }

    [Fact]
    public async Task StartRewatchAsync_ForCompletedAnime_IncrementsCounter_ResetsProgress_AndSetsStatusToWatching()
    {
        var item = new MediaItem
        {
            Title = "Death Note",
            Type = MediaType.Anime,
            Status = MediaStatus.Completed,
            CurrentProgress = 37,
            TotalProgress = 37,
            RewatchCount = 0
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.StartRewatchAsync(id);

        Assert.Equal(1, result.RewatchCount);
        Assert.Equal(0, result.CurrentProgress);
        Assert.Equal(MediaStatus.Watching, result.Status);
        Assert.Equal(37, result.TotalProgress);
    }

    [Theory]
    [InlineData(MediaType.Manga)]
    [InlineData(MediaType.Manhwa)]
    public async Task StartRewatchAsync_ForCompletedMangaOrManhwa_SetsStatusToReading(MediaType mediaType)
    {
        var item = new MediaItem
        {
            Title = "Solo Leveling",
            Type = mediaType,
            Status = MediaStatus.Completed,
            CurrentProgress = 179,
            TotalProgress = 179,
            RewatchCount = 2
        };
        var id = await _repository.AddAsync(item);

        var result = await _service.StartRewatchAsync(id);

        Assert.Equal(3, result.RewatchCount);
        Assert.Equal(0, result.CurrentProgress);
        Assert.Equal(MediaStatus.Reading, result.Status);
    }

    [Fact]
    public async Task StartRewatchAsync_WhenNotCompleted_ThrowsInvalidOperationException()
    {
        var item = new MediaItem
        {
            Title = "Currently Watching Series",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 5,
            TotalProgress = 12,
            RewatchCount = 0
        };
        var id = await _repository.AddAsync(item);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartRewatchAsync(id));
    }

    [Fact]
    public async Task Operations_PersistChangesToDatabase_AndAreReadBackCorrectly()
    {
        var item = new MediaItem
        {
            Title = "Berserk",
            Type = MediaType.Manga,
            Status = MediaStatus.Reading,
            CurrentProgress = 10,
            TotalProgress = 364,
            RewatchCount = 0
        };
        var id = await _repository.AddAsync(item);

        await _service.IncrementProgressAsync(id);
        await _service.IncrementProgressAsync(id);
        await _service.CompleteAsync(id);
        await _service.StartRewatchAsync(id);

        // Read directly from repository to verify database state
        var reloaded = await _repository.GetByIdAsync(id);
        Assert.NotNull(reloaded);
        Assert.Equal(MediaStatus.Reading, reloaded.Status);
        Assert.Equal(0, reloaded.CurrentProgress);
        Assert.Equal(1, reloaded.RewatchCount);
        Assert.Equal(364, reloaded.TotalProgress);
    }
}
