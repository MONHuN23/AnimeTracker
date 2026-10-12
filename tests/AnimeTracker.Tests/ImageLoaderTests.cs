using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Services;
using Xunit;

namespace AnimeTracker.Tests;

public class ImageLoaderTests : IDisposable
{
    private readonly string _tempDir;
    // 1x1 transparent PNG
    private static readonly byte[] Valid1x1Png = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    };

    static ImageLoaderTests()
    {
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
        }
        catch
        {
            // Already initialized or platform unavailable
        }
    }

    public ImageLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"animetracker_imgtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Fact]
    public void LoadFromBytes_ValidPngBytes_ReturnsBitmap()
    {
        var bmp = ImageLoader.LoadFromBytes(Valid1x1Png);
        Assert.NotNull(bmp);
        Assert.Equal(1, bmp.PixelSize.Width);
        Assert.Equal(1, bmp.PixelSize.Height);
    }

    [Fact]
    public void LoadFromBytes_CorruptBytes_ReturnsNullWithoutThrowing()
    {
        var corrupt = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 };
        var bmp = ImageLoader.LoadFromBytes(corrupt);
        Assert.Null(bmp);
    }

    [Fact]
    public void LoadFromFile_ValidFile_ReturnsBitmap()
    {
        var filePath = Path.Combine(_tempDir, "test.png");
        File.WriteAllBytes(filePath, Valid1x1Png);

        var bmp = ImageLoader.LoadFromFile(filePath);
        Assert.NotNull(bmp);
    }

    [Fact]
    public void LoadFromFile_MissingFile_ReturnsNull()
    {
        var bmp = ImageLoader.LoadFromFile(Path.Combine(_tempDir, "nonexistent.png"));
        Assert.Null(bmp);
    }

    [Fact]
    public async Task LoadCoverAsync_LocalFileExists_LoadsLocalImage()
    {
        var coversDir = Path.Combine(_tempDir, "covers");
        Directory.CreateDirectory(coversDir);
        var coverFile = Path.Combine(coversDir, "local_cover.png");
        await File.WriteAllBytesAsync(coverFile, Valid1x1Png);

        var bmp = await ImageLoader.LoadCoverAsync("covers/local_cover.png", null, _tempDir);
        Assert.NotNull(bmp);
    }

    [Fact]
    public async Task LoadCoverAsync_CorruptLocalFile_FallsBackGracefullyWithoutCrash()
    {
        var coversDir = Path.Combine(_tempDir, "covers");
        Directory.CreateDirectory(coversDir);
        var coverFile = Path.Combine(coversDir, "corrupt.png");
        await File.WriteAllBytesAsync(coverFile, new byte[] { 1, 2, 3 });

        // Corrupt local file, no remote URL -> returns null safely without throwing
        var bmp = await ImageLoader.LoadCoverAsync("covers/corrupt.png", null, _tempDir);
        Assert.Null(bmp);
    }
}
