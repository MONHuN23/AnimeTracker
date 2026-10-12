using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class MediaItemRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;

    public MediaItemRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
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
    public void InitializeDatabase_CreatesDatabaseAndTableSuccessfully()
    {
        DatabaseInitializer.Initialize(_connectionString);

        Assert.True(File.Exists(_tempDbPath));

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='MediaItems';";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InitializeDatabase_RepeatedCall_PreservesExistingData()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item = new MediaItem
        {
            Title = "Frieren: Beyond Journey's End",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 10,
            TotalProgress = 28
        };
        await repository.AddAsync(item);

        // Re-initialize database
        DatabaseInitializer.Initialize(_connectionString);

        var items = await repository.GetAllAsync();
        Assert.Single(items);
        Assert.Equal("Frieren: Beyond Journey's End", items[0].Title);
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_InsertsAndRetrievesItemWithAllFields()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item = new MediaItem
        {
            MalId = 52991,
            MalType = "anime",
            Title = "Frieren",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CurrentProgress = 8,
            TotalProgress = 28,
            Score = 10,
            RewatchCount = 1,
            CoverImageUrl = "https://example.com/frieren.jpg",
            Genres = "Adventure, Drama, Fantasy",
            Synopsis = "Elf mage journey",
            ReleaseYear = 2023
        };

        var id = await repository.AddAsync(item);
        Assert.True(id > 0);

        var retrieved = await repository.GetByIdAsync(id);
        Assert.NotNull(retrieved);
        Assert.Equal(id, retrieved.Id);
        Assert.Equal(52991, retrieved.MalId);
        Assert.Equal("anime", retrieved.MalType);
        Assert.Equal("Frieren", retrieved.Title);
        Assert.Equal(MediaType.Anime, retrieved.Type);
        Assert.Equal(MediaStatus.Watching, retrieved.Status);
        Assert.Equal(8, retrieved.CurrentProgress);
        Assert.Equal(28, retrieved.TotalProgress);
        Assert.Equal(10, retrieved.Score);
        Assert.Equal(1, retrieved.RewatchCount);
        Assert.Equal("https://example.com/frieren.jpg", retrieved.CoverImageUrl);
        Assert.Equal("Adventure, Drama, Fantasy", retrieved.Genres);
        Assert.Equal("Elf mage journey", retrieved.Synopsis);
        Assert.Equal(2023, retrieved.ReleaseYear);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesItemSuccessfully()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item = new MediaItem
        {
            Title = "Solo Leveling",
            Type = MediaType.Manhwa,
            Status = MediaStatus.Reading,
            CurrentProgress = 40,
            TotalProgress = 179
        };
        var id = await repository.AddAsync(item);
        item.Id = id;

        item.CurrentProgress = 50;
        item.Status = MediaStatus.Completed;
        item.Score = 9;
        item.RewatchCount = 1;

        var updated = await repository.UpdateAsync(item);
        Assert.True(updated);

        var retrieved = await repository.GetByIdAsync(id);
        Assert.NotNull(retrieved);
        Assert.Equal(50, retrieved.CurrentProgress);
        Assert.Equal(MediaStatus.Completed, retrieved.Status);
        Assert.Equal(9, retrieved.Score);
        Assert.Equal(1, retrieved.RewatchCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesItemFromDatabase()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item = new MediaItem
        {
            Title = "Chainsaw Man",
            Type = MediaType.Manga,
            Status = MediaStatus.PlanToRead
        };
        var id = await repository.AddAsync(item);

        var deleted = await repository.DeleteAsync(id);
        Assert.True(deleted);

        var retrieved = await repository.GetByIdAsync(id);
        Assert.Null(retrieved);

        var all = await repository.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task AddAsync_DuplicateMalIdAndType_ThrowsSqliteException()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item1 = new MediaItem
        {
            MalId = 1234,
            MalType = "anime",
            Title = "Original Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching
        };
        await repository.AddAsync(item1);

        var item2 = new MediaItem
        {
            MalId = 1234,
            MalType = "anime",
            Title = "Duplicate Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.PlanToWatch
        };

        await Assert.ThrowsAsync<SqliteException>(async () => await repository.AddAsync(item2));
    }

    [Fact]
    public async Task AddAsync_SameMalIdDifferentMalType_Succeeds()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var anime = new MediaItem
        {
            MalId = 100,
            MalType = "anime",
            Title = "Series Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching
        };
        var manga = new MediaItem
        {
            MalId = 100,
            MalType = "manga",
            Title = "Series Manga",
            Type = MediaType.Manga,
            Status = MediaStatus.Reading
        };

        var id1 = await repository.AddAsync(anime);
        var id2 = await repository.AddAsync(manga);

        Assert.True(id1 > 0);
        Assert.True(id2 > 0);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task AddAsync_MultipleManualEntriesWithoutCatalogId_Succeeds()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item1 = new MediaItem
        {
            MalId = null,
            MalType = null,
            Title = "Custom Webcomic",
            Type = MediaType.Manhwa,
            Status = MediaStatus.Reading
        };
        var item2 = new MediaItem
        {
            MalId = null,
            MalType = null,
            Title = "Custom Indie Comic",
            Type = MediaType.Manga,
            Status = MediaStatus.PlanToRead
        };

        var id1 = await repository.AddAsync(item1);
        var id2 = await repository.AddAsync(item2);

        Assert.True(id1 > 0);
        Assert.True(id2 > 0);

        var all = await repository.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task AddAsync_WithCoverImagePath_PersistsAndRetrievesRelativePath()
    {
        DatabaseInitializer.Initialize(_connectionString);
        var repository = new MediaItemRepository(_connectionString);

        var item = new MediaItem
        {
            Title = "Solo Leveling Custom",
            Type = MediaType.Manhwa,
            Status = MediaStatus.Reading,
            CoverImagePath = "covers/cover_abc123.png"
        };

        var id = await repository.AddAsync(item);
        var retrieved = await repository.GetByIdAsync(id);

        Assert.NotNull(retrieved);
        Assert.Equal("covers/cover_abc123.png", retrieved.CoverImagePath);

        // Update CoverImagePath
        retrieved.CoverImagePath = "covers/cover_updated.jpg";
        await repository.UpdateAsync(retrieved);

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("covers/cover_updated.jpg", updated.CoverImagePath);
    }

    [Fact]
    public async Task Migration_LegacyDatabaseWithoutCoverImagePath_MigratesCleanlyAndRetainsData()
    {
        // 1. Create legacy table without CoverImagePath column
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE MediaItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MalId INTEGER,
                    MalType TEXT,
                    Title TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CurrentProgress INTEGER NOT NULL DEFAULT 0,
                    TotalProgress INTEGER,
                    Score INTEGER,
                    RewatchCount INTEGER NOT NULL DEFAULT 0,
                    CoverImageUrl TEXT,
                    Genres TEXT,
                    Synopsis TEXT,
                    ReleaseYear INTEGER,
                    UpdatedAt TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                INSERT INTO MediaItems (Title, Type, Status, CurrentProgress, UpdatedAt, CreatedAt)
                VALUES ('Legacy Title', 'Anime', 'Watching', 3, '2026-01-01', '2026-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        // 2. Run DatabaseInitializer migration
        DatabaseInitializer.Initialize(_connectionString);

        // 3. Verify existing legacy data is intact
        var repository = new MediaItemRepository(_connectionString);
        var items = await repository.GetAllAsync();
        Assert.Single(items);
        Assert.Equal("Legacy Title", items[0].Title);
        Assert.Null(items[0].CoverImagePath);

        // 4. Verify we can now insert an item with CoverImagePath
        var newItem = new MediaItem
        {
            Title = "Migrated Title With Cover",
            Type = MediaType.Manga,
            Status = MediaStatus.Reading,
            CoverImagePath = "covers/test_migrated.png"
        };
        var id = await repository.AddAsync(newItem);
        var retrieved = await repository.GetByIdAsync(id);
        Assert.NotNull(retrieved);
        Assert.Equal("covers/test_migrated.png", retrieved.CoverImagePath);
    }
}
