using System;
using System.IO;
using System.Threading.Tasks;
using AnimeTracker.Data;

namespace AnimeTracker.Services;

public class CoverStorageService
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public string BaseDataDirectory { get; }
    public string CoversDirectory => Path.Combine(BaseDataDirectory, "covers");

    public CoverStorageService(string? baseDataDirectory = null)
    {
        BaseDataDirectory = baseDataDirectory ?? DatabaseInitializer.GetDefaultDataDirectory();
    }

    public bool ValidateImage(byte[] data, out string? extension, out string? errorMessage)
    {
        extension = null;
        errorMessage = null;

        if (data == null || data.Length == 0)
        {
            errorMessage = "Image file is empty.";
            return false;
        }

        if (data.Length > MaxFileSizeBytes)
        {
            errorMessage = $"Image file exceeds maximum allowed size of 10 MB ({data.Length / (1024.0 * 1024.0):F2} MB).";
            return false;
        }

        // JPEG: FF D8 FF
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            extension = ".jpg";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            extension = ".png";
            return true;
        }

        // WebP: 'RIFF' .... 'WEBP'
        if (data.Length >= 12 &&
            data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
        {
            extension = ".webp";
            return true;
        }

        errorMessage = "Unsupported image format. Please select a valid PNG, JPEG, or WebP image.";
        return false;
    }

    public bool ValidateFile(string filePath, out string? errorMessage)
    {
        errorMessage = null;
        if (!File.Exists(filePath))
        {
            errorMessage = "File not found.";
            return false;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (info.Length > MaxFileSizeBytes)
            {
                errorMessage = $"Image file exceeds maximum allowed size of 10 MB ({info.Length / (1024.0 * 1024.0):F2} MB).";
                return false;
            }

            var bytes = File.ReadAllBytes(filePath);
            return ValidateImage(bytes, out _, out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = $"Error reading file: {ex.Message}";
            return false;
        }
    }

    public async Task<string> SaveCoverAsync(byte[] imageData)
    {
        if (!ValidateImage(imageData, out var extension, out var errorMessage))
        {
            throw new ArgumentException(errorMessage ?? "Invalid image data.");
        }

        if (!Directory.Exists(CoversDirectory))
        {
            Directory.CreateDirectory(CoversDirectory);
        }

        var fileName = $"cover_{Guid.NewGuid():N}{extension}";
        var relativePath = Path.Combine("covers", fileName).Replace('\\', '/');
        var fullPath = GetFullPath(relativePath);

        await File.WriteAllBytesAsync(fullPath, imageData);
        return relativePath;
    }

    public async Task<string> SaveCoverFromFileAsync(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Source image file not found: {sourceFilePath}");
        }

        var fileInfo = new FileInfo(sourceFilePath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            throw new ArgumentException($"Image file exceeds maximum allowed size of 10 MB ({fileInfo.Length / (1024.0 * 1024.0):F2} MB).");
        }

        var bytes = await File.ReadAllBytesAsync(sourceFilePath);
        return await SaveCoverAsync(bytes);
    }

    public void DeleteCover(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        try
        {
            var fullPath = GetFullPath(relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public string GetFullPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }

        return Path.Combine(BaseDataDirectory, relativePath);
    }

    public async Task<byte[]?> GetCoverBytesAsync(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath)) return null;

        try
        {
            return await File.ReadAllBytesAsync(fullPath);
        }
        catch
        {
            return null;
        }
    }
}
