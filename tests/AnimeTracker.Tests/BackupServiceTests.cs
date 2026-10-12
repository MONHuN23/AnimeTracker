using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.Services.Backup;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AnimeTracker.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _connectionString;
    private readonly MediaItemRepository _repository;
    private readonly CoverStorageService _coverStorageService;
    private readonly BackupService _backupService;
    private readonly string _tempDirectory;

    public BackupServiceTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"animetracker_backup_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_tempDbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _repository = new MediaItemRepository(_connectionString);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"animetracker_backup_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _coverStorageService = new CoverStorageService(_tempDirectory);
        _backupService = new BackupService(_connectionString, _coverStorageService);
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
                // Best effort
            }
        }

        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Fact]
    public async Task ExportToFileAsync_WritesValidFormattedJsonWithMetadata()
    {
        var items = new List<MediaItem>
        {
            new()
            {
                Id = 1,
                MalId = 52991,
                MalType = "anime",
                Title = "Frieren",
                Type = MediaType.Anime,
                Status = MediaStatus.Watching,
                CurrentProgress = 12,
                TotalProgress = 28,
                Score = 10,
                RewatchCount = 1,
                Genres = "Adventure, Fantasy",
                Synopsis = "Elf journey",
                ReleaseYear = 2023,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var filePath = Path.Combine(_tempDirectory, "export.json");
        await _backupService.ExportToFileAsync(filePath, items);

        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
        Assert.True(root.TryGetProperty("exportedAt", out _));

        var jsonItems = root.GetProperty("items");
        Assert.Equal(1, jsonItems.GetArrayLength());
        var first = jsonItems[0];
        Assert.Equal("Frieren", first.GetProperty("title").GetString());
        Assert.Equal("Anime", first.GetProperty("type").GetString());
        Assert.Equal("Watching", first.GetProperty("status").GetString());
        Assert.Equal(10, first.GetProperty("score").GetInt32());
    }

    [Fact]
    public void ValidateJson_RejectsInvalidJsonSyntax()
    {
        var invalidJson = "{ invalid json here... ";
        var result = _backupService.ValidateJson(invalidJson);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateJson_RejectsUnsupportedVersion()
    {
        var json = """
        {
            "version": 99,
            "totalCount": 0,
            "items": []
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("version", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateJson_RejectsEmptyTitle()
    {
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "title": "   ",
                    "type": "Anime",
                    "status": "Watching"
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("title", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateJson_RejectsScoreOutOfRange()
    {
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "title": "Test",
                    "type": "Anime",
                    "status": "Watching",
                    "score": 15
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("score", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateJson_RejectsNegativeProgressOrTotalProgressLessThanCurrent()
    {
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "title": "Test",
                    "type": "Anime",
                    "status": "Watching",
                    "currentProgress": 10,
                    "totalProgress": 5
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("progress", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateJson_RejectsIncompatibleStatusForMediaType()
    {
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "title": "Test Anime",
                    "type": "Anime",
                    "status": "Reading"
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("status", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateJson_RejectsDuplicateMalIdAndType()
    {
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "malId": 100,
                    "malType": "anime",
                    "title": "Show A",
                    "type": "Anime",
                    "status": "Watching"
                },
                {
                    "malId": 100,
                    "malType": "anime",
                    "title": "Show B",
                    "type": "Anime",
                    "status": "PlanToWatch"
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("duplicate", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportReplaceAsync_ReplacesAllExistingItemsAtomically()
    {
        // 1. Existing items in database
        await _repository.AddAsync(new MediaItem { Title = "Old Item 1", Type = MediaType.Anime, Status = MediaStatus.Watching });
        await _repository.AddAsync(new MediaItem { Title = "Old Item 2", Type = MediaType.Manga, Status = MediaStatus.Reading });
        var initialAll = await _repository.GetAllAsync();
        Assert.Equal(2, initialAll.Count);

        // 2. New items to import
        var newItems = new List<MediaItem>
        {
            new()
            {
                Title = "Imported Item A",
                Type = MediaType.Anime,
                Status = MediaStatus.Completed,
                Score = 9,
                CurrentProgress = 12,
                TotalProgress = 12
            },
            new()
            {
                Title = "Imported Item B",
                Type = MediaType.Manga,
                Status = MediaStatus.PlanToRead
            }
        };

        await _backupService.ImportReplaceAsync(newItems);

        var finalAll = await _repository.GetAllAsync();
        Assert.Equal(2, finalAll.Count);
        Assert.DoesNotContain(finalAll, x => x.Title.StartsWith("Old Item"));
        Assert.Contains(finalAll, x => x.Title == "Imported Item A");
        Assert.Contains(finalAll, x => x.Title == "Imported Item B");
    }

    [Fact]
    public async Task ImportReplaceAsync_RollsBack_WhenDatabaseErrorOccurs()
    {
        // 1. Existing item in database
        await _repository.AddAsync(new MediaItem { Title = "Preserved Item", Type = MediaType.Anime, Status = MediaStatus.Watching });
        var initialCount = (await _repository.GetAllAsync()).Count;
        Assert.Equal(1, initialCount);

        // 2. Prepare items where the second item violates the unique index on (MalId, MalType)
        // because both have identical MalId 999 and MalType "anime"
        var itemsWithDuplicate = new List<MediaItem>
        {
            new() { Title = "Valid New", Type = MediaType.Anime, Status = MediaStatus.Watching, MalId = 999, MalType = "anime" },
            new() { Title = "Duplicate New", Type = MediaType.Anime, Status = MediaStatus.Watching, MalId = 999, MalType = "anime" }
        };

        // Execution should throw SQLite exception
        await Assert.ThrowsAnyAsync<Exception>(() => _backupService.ImportReplaceAsync(itemsWithDuplicate));

        // 3. Verify rollback: original library is completely preserved
        var preservedItems = await _repository.GetAllAsync();
        Assert.Single(preservedItems);
        Assert.Equal("Preserved Item", preservedItems[0].Title);
    }

    [Fact]
    public async Task ExportToFileAsync_WithLocalCover_IncludesBase64CoverData()
    {
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        var relativeCoverPath = await _coverStorageService.SaveCoverAsync(validPng);

        var items = new List<MediaItem>
        {
            new()
            {
                Title = "Cover Anime",
                Type = MediaType.Anime,
                Status = MediaStatus.Watching,
                CoverImagePath = relativeCoverPath
            }
        };

        var filePath = Path.Combine(_tempDirectory, "export_with_cover.json");
        await _backupService.ExportToFileAsync(filePath, items);

        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);

        using var doc = JsonDocument.Parse(content);
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.True(item.TryGetProperty("coverImageBase64", out var base64Prop));
        Assert.False(string.IsNullOrWhiteSpace(base64Prop.GetString()));

        var decodedBytes = Convert.FromBase64String(base64Prop.GetString()!);
        Assert.Equal(validPng, decodedBytes);
    }

    [Fact]
    public void ValidateJson_RejectsInvalidBase64CoverData()
    {
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "title": "Bad Cover",
                    "type": "Anime",
                    "status": "Watching",
                    "coverImageBase64": "not_valid_base_64!@#$"
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("base64", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateJson_RejectsInvalidImageBytesInBase64()
    {
        // "AQID" is [1, 2, 3], not a valid PNG/JPEG/WebP
        var json = """
        {
            "version": 1,
            "items": [
                {
                    "title": "Bad Image Bytes",
                    "type": "Anime",
                    "status": "Watching",
                    "coverImageBase64": "AQID"
                }
            ]
        }
        """;

        var result = _backupService.ValidateJson(json);
        Assert.False(result.IsValid);
        Assert.Contains("format", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportReplaceAsync_WithBase64Cover_SavesCoverFileAndSetsCoverImagePath()
    {
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        var base64String = Convert.ToBase64String(validPng);

        var json = $$"""
        {
            "version": 1,
            "items": [
                {
                    "title": "Imported Cover Title",
                    "type": "Anime",
                    "status": "Watching",
                    "coverImageBase64": "{{base64String}}"
                }
            ]
        }
        """;

        var validationResult = _backupService.ValidateJson(json);
        Assert.True(validationResult.IsValid);
        Assert.NotNull(validationResult.ValidatedItems);

        await _backupService.ImportReplaceAsync(validationResult.ValidatedItems);

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        var imported = all[0];
        Assert.NotNull(imported.CoverImagePath);
        Assert.StartsWith("covers/", imported.CoverImagePath!);

        var fullPath = _coverStorageService.GetFullPath(imported.CoverImagePath!);
        Assert.True(File.Exists(fullPath));
        var diskBytes = await File.ReadAllBytesAsync(fullPath);
        Assert.Equal(validPng, diskBytes);
    }

    [Fact]
    public async Task ImportReplaceAsync_WhenDatabaseFails_DeletesNewlySavedCoverFiles()
    {
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
        var base64String = Convert.ToBase64String(validPng);

        // Prepare an item with cover, and a second item that causes a duplicate (MalId, MalType) DB collision
        var items = new List<MediaItem>
        {
            new()
            {
                Title = "First Title With Cover",
                Type = MediaType.Anime,
                Status = MediaStatus.Watching,
                MalId = 111,
                MalType = "anime",
                PendingCoverImageData = validPng
            },
            new()
            {
                Title = "Duplicate Title To Fail DB",
                Type = MediaType.Anime,
                Status = MediaStatus.Watching,
                MalId = 111,
                MalType = "anime"
            }
        };

        // Execution should throw DB exception
        await Assert.ThrowsAnyAsync<Exception>(() => _backupService.ImportReplaceAsync(items));

        // Covers directory should not have orphan files created during the failed transaction
        if (Directory.Exists(_coverStorageService.CoversDirectory))
        {
            var files = Directory.GetFiles(_coverStorageService.CoversDirectory);
            Assert.Empty(files);
        }
    }
}
