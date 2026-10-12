using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeTracker.Services.Jikan;

public class JikanApiClient
{
    private readonly HttpClient _httpClient;
    public const string DefaultBaseUrl = "https://api.jikan.moe/v4/";

    public JikanApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(DefaultBaseUrl) };
    }

    public Task<IReadOnlyList<JikanSearchResult>> SearchAnimeAsync(string? query, CancellationToken cancellationToken = default)
    {
        return SearchAsync("anime", query, cancellationToken);
    }

    public Task<IReadOnlyList<JikanSearchResult>> SearchMangaAsync(string? query, CancellationToken cancellationToken = default)
    {
        return SearchAsync("manga", query, cancellationToken);
    }

    public async Task<IReadOnlyList<JikanSearchResult>> SearchAsync(string malType, string? query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<JikanSearchResult>();
        }

        var trimmed = query.Trim();
        var relativeUrl = $"{malType}?q={Uri.EscapeDataString(trimmed)}";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JikanApiException($"Network error while calling Jikan API ({malType}): {ex.Message}", innerException: ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new JikanApiException("Jikan API rate limit reached (HTTP 429).", HttpStatusCode.TooManyRequests);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new JikanApiException($"Jikan API returned error status: {response.StatusCode}", response.StatusCode);
            }

            string content;
            try
            {
                content = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw new JikanApiException($"Failed to read Jikan API response content: {ex.Message}", response.StatusCode, ex);
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<JikanSearchResult>();
                }

                var results = new List<JikanSearchResult>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    results.Add(ParseItem(item, malType));
                }

                return results;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse Jikan API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    private static JikanSearchResult ParseItem(JsonElement item, string malType)
    {
        var malId = item.TryGetProperty("mal_id", out var malIdProp) && malIdProp.TryGetInt32(out var idVal)
            ? idVal
            : 0;

        var title = item.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
            ? titleProp.GetString() ?? string.Empty
            : string.Empty;

        var englishTitle = item.TryGetProperty("title_english", out var engTitleProp) && engTitleProp.ValueKind == JsonValueKind.String
            ? engTitleProp.GetString()
            : null;

        string? coverUrl = null;
        if (item.TryGetProperty("images", out var imagesProp) && imagesProp.ValueKind == JsonValueKind.Object)
        {
            if (imagesProp.TryGetProperty("webp", out var webpProp) && webpProp.ValueKind == JsonValueKind.Object)
            {
                coverUrl = GetStringOrNull(webpProp, "large_image_url") ?? GetStringOrNull(webpProp, "image_url");
            }

            if (string.IsNullOrEmpty(coverUrl) && imagesProp.TryGetProperty("jpg", out var jpgProp) && jpgProp.ValueKind == JsonValueKind.Object)
            {
                coverUrl = GetStringOrNull(jpgProp, "large_image_url") ?? GetStringOrNull(jpgProp, "image_url");
            }
        }

        int? totalCount = null;
        if (string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase))
        {
            if (item.TryGetProperty("episodes", out var epProp) && epProp.TryGetInt32(out var eps))
            {
                totalCount = eps;
            }
        }
        else
        {
            if (item.TryGetProperty("chapters", out var chProp) && chProp.TryGetInt32(out var chs))
            {
                totalCount = chs;
            }
        }

        var synopsis = GetStringOrNull(item, "synopsis");

        double? score = null;
        if (item.TryGetProperty("score", out var scoreProp) && scoreProp.TryGetDouble(out var sVal))
        {
            score = sVal;
        }

        int? year = null;
        if (item.TryGetProperty("year", out var yearProp) && yearProp.TryGetInt32(out var yVal))
        {
            year = yVal;
        }

        var genresList = new List<string>();
        if (item.TryGetProperty("genres", out var genresProp) && genresProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in genresProp.EnumerateArray())
            {
                var gName = GetStringOrNull(g, "name");
                if (!string.IsNullOrEmpty(gName))
                {
                    genresList.Add(gName);
                }
            }
        }

        return new JikanSearchResult
        {
            MalId = malId,
            MalType = malType,
            Title = title,
            EnglishTitle = englishTitle,
            CoverImageUrl = coverUrl,
            TotalCount = totalCount,
            Synopsis = synopsis,
            Genres = genresList,
            Score = score,
            Year = year
        };
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }
}
