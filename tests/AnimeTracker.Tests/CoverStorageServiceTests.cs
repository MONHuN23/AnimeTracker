using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Services;
using Xunit;

namespace AnimeTracker.Tests;

public class CoverStorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CoverStorageService _service;

    // Standard valid magic bytes
    private static readonly byte[] ValidJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
    private static readonly byte[] ValidPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
    private static readonly byte[] ValidWebp = new byte[]
    {
        0x52, 0x49, 0x46, 0x46, // 'RIFF'
        0x20, 0x00, 0x00, 0x00, // file size
        0x57, 0x45, 0x42, 0x50, // 'WEBP'
        0x56, 0x50, 0x38, 0x20  // 'VP8 '
    };

    public CoverStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"animetracker_covertest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new CoverStorageService(_tempDir);
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
    public void ValidateImage_ValidJpeg_ReturnsTrueAndJpgExtension()
    {
        var valid = _service.ValidateImage(ValidJpeg, out var ext, out var error);

        Assert.True(valid);
        Assert.Equal(".jpg", ext);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateImage_ValidPng_ReturnsTrueAndPngExtension()
    {
        var valid = _service.ValidateImage(ValidPng, out var ext, out var error);

        Assert.True(valid);
        Assert.Equal(".png", ext);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateImage_ValidWebp_ReturnsTrueAndWebpExtension()
    {
        var valid = _service.ValidateImage(ValidWebp, out var ext, out var error);

        Assert.True(valid);
        Assert.Equal(".webp", ext);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateImage_EmptyBytes_ReturnsFalse()
    {
        var valid = _service.ValidateImage(Array.Empty<byte>(), out var ext, out var error);

        Assert.False(valid);
        Assert.Null(ext);
        Assert.NotNull(error);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImage_InvalidMagicBytes_ReturnsFalse()
    {
        var fakeImage = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var valid = _service.ValidateImage(fakeImage, out var ext, out var error);

        Assert.False(valid);
        Assert.Null(ext);
        Assert.NotNull(error);
        Assert.Contains("unsupported", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImage_Exceeds10MB_ReturnsFalse()
    {
        // Allocate a header with size exceeding 10MB
        var large = new byte[CoverStorageService.MaxFileSizeBytes + 1];
        Buffer.BlockCopy(ValidJpeg, 0, large, 0, ValidJpeg.Length);

        var valid = _service.ValidateImage(large, out var ext, out var error);

        Assert.False(valid);
        Assert.Null(ext);
        Assert.NotNull(error);
        Assert.Contains("10 MB", error);
    }

    [Fact]
    public async Task SaveCoverAsync_ValidPng_SavesFileWithRelativePath()
    {
        var relativePath = await _service.SaveCoverAsync(ValidPng);

        Assert.NotNull(relativePath);
        Assert.StartsWith("covers", relativePath);
        Assert.EndsWith(".png", relativePath);

        var fullPath = _service.GetFullPath(relativePath);
        Assert.True(File.Exists(fullPath));

        var savedBytes = await File.ReadAllBytesAsync(fullPath);
        Assert.Equal(ValidPng, savedBytes);
    }

    [Fact]
    public async Task SaveCoverFromFileAsync_CopiesFileWithoutOverwritingOriginal()
    {
        var originalPath = Path.Combine(_tempDir, "source_original.jpg");
        await File.WriteAllBytesAsync(originalPath, ValidJpeg);

        var relativePath = await _service.SaveCoverFromFileAsync(originalPath);

        Assert.True(File.Exists(originalPath)); // Original remains intact
        Assert.NotNull(relativePath);

        var fullPath = _service.GetFullPath(relativePath);
        Assert.True(File.Exists(fullPath));
        Assert.NotEqual(originalPath, fullPath);
    }

    [Fact]
    public async Task DeleteCover_RemovesFile_AndIsIdempotent()
    {
        var relativePath = await _service.SaveCoverAsync(ValidJpeg);
        var fullPath = _service.GetFullPath(relativePath);
        Assert.True(File.Exists(fullPath));

        _service.DeleteCover(relativePath);
        Assert.False(File.Exists(fullPath));

        // Calling again should not throw
        _service.DeleteCover(relativePath);
        _service.DeleteCover(null);
    }

    [Fact]
    public async Task GetCoverBytesAsync_ReturnsBytes_WhenFileExists()
    {
        var relativePath = await _service.SaveCoverAsync(ValidPng);
        var bytes = await _service.GetCoverBytesAsync(relativePath);

        Assert.NotNull(bytes);
        Assert.Equal(ValidPng, bytes);

        var nonExistent = await _service.GetCoverBytesAsync("covers/nonexistent.png");
        Assert.Null(nonExistent);
    }
}
