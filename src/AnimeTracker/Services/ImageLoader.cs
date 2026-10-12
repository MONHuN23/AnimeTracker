using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AnimeTracker.Data;
using Avalonia.Media.Imaging;

namespace AnimeTracker.Services;

public static class ImageLoader
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static Bitmap? LoadFromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap? LoadFromFile(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;

        try
        {
            return new Bitmap(fullPath);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<Bitmap?> LoadCoverAsync(string? localRelativePath, string? remoteUrl, string? baseDataDirectory = null)
    {
        // 1. Try local cover image first
        if (!string.IsNullOrWhiteSpace(localRelativePath))
        {
            try
            {
                var baseDir = baseDataDirectory ?? DatabaseInitializer.GetDefaultDataDirectory();
                var fullPath = Path.IsPathRooted(localRelativePath)
                    ? localRelativePath
                    : Path.Combine(baseDir, localRelativePath);

                var localBitmap = LoadFromFile(fullPath);
                if (localBitmap != null)
                {
                    return localBitmap;
                }
            }
            catch
            {
                // Fall back gracefully
            }
        }

        // 2. If no local image or local image failed/missing, fall back to remote URL
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            return await LoadFromUrlAsync(remoteUrl);
        }

        return null;
    }

    public static async Task<Bitmap?> LoadFromUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            // Fallback gracefully on network error, invalid URL, or offline
            return null;
        }
    }
}
