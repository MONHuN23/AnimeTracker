using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Data;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;
using Xunit;

namespace AnimeTracker.Tests;

public class MediaCardViewModelTests : IDisposable
{
    private static readonly byte[] MinimalPng =
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

    private readonly string _tempDir;
    private readonly MediaTrackingService _trackingService;

    static MediaCardViewModelTests()
    {
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
        }
        catch
        {
            // Already initialized or headless
        }
    }

    public MediaCardViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"animetracker_card_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "test.db");
        var connStr = $"Data Source={dbPath}";
        DatabaseInitializer.Initialize(connStr);
        var repo = new MediaItemRepository(connStr);
        _trackingService = new MediaTrackingService(repo);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [Fact]
    public void MediaCardViewModel_ExposesCoverImagePath()
    {
        var item = new MediaItem
        {
            Id = 1,
            Title = "Test Anime",
            Type = MediaType.Anime,
            Status = MediaStatus.Watching,
            CoverImagePath = "covers/test_cover.png"
        };

        var vm = new MediaCardViewModel(item, _trackingService);

        Assert.Equal("covers/test_cover.png", vm.CoverImagePath);
    }

    [Fact]
    public async Task LoadCoverImage_WhenLocalCoverExists_LoadsLocalImage()
    {
        var baseDir = DatabaseInitializer.GetDefaultDataDirectory();
        var coversDir = Path.Combine(baseDir, "covers");
        Directory.CreateDirectory(coversDir);
        var filename = $"test_card_{Guid.NewGuid():N}.png";
        var fullPath = Path.Combine(coversDir, filename);
        await File.WriteAllBytesAsync(fullPath, MinimalPng);

        try
        {
            var item = new MediaItem
            {
                Id = 1,
                Title = "Test Anime",
                Type = MediaType.Anime,
                Status = MediaStatus.Watching,
                CoverImagePath = Path.Combine("covers", filename)
            };

            var vm = new MediaCardViewModel(item, _trackingService);
            await vm.LoadCoverImageAsync();

            Assert.NotNull(vm.CoverImage);
        }
        finally
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }
}
