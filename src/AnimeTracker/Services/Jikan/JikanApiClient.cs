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
    private readonly HttpClient _kitsuClient;
    private readonly bool _isCustomClient;
    private bool _jikanOffline;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Timestamp, IReadOnlyList<JikanSearchResult> Items)> _genreCache = new();

    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    public static readonly IReadOnlyDictionary<string, int> JikanGenreIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = 1,
        ["Adventure"] = 2,
        ["Cars"] = 3,
        ["Comedy"] = 4,
        ["Avante Garde"] = 5,
        ["Demons"] = 6,
        ["Mystery"] = 7,
        ["Drama"] = 8,
        ["Ecchi"] = 9,
        ["Fantasy"] = 10,
        ["Game"] = 11,
        ["Hentai"] = 12,
        ["Historical"] = 13,
        ["Horror"] = 14,
        ["Kids"] = 15,
        ["Martial Arts"] = 17,
        ["Mecha"] = 18,
        ["Music"] = 19,
        ["Parody"] = 20,
        ["Samurai"] = 21,
        ["Romance"] = 22,
        ["School"] = 23,
        ["Sci-Fi"] = 24,
        ["Shoujo"] = 25,
        ["Girls Love"] = 26,
        ["Shounen"] = 27,
        ["Boys Love"] = 28,
        ["Space"] = 29,
        ["Sports"] = 30,
        ["Super Power"] = 31,
        ["Vampire"] = 32,
        ["Harem"] = 35,
        ["Slice of Life"] = 36,
        ["Supernatural"] = 37,
        ["Military"] = 38,
        ["Police"] = 39,
        ["Psychological"] = 40,
        ["Suspense"] = 41,
        ["Seinen"] = 42,
        ["Josei"] = 43,
        ["Award Winning"] = 46,
        ["Gourmet"] = 47,
        ["Work Life"] = 48
    };

    public const string DefaultBaseUrl = "https://api.jikan.moe/v4/";
    public const string DefaultKitsuBaseUrl = "https://kitsu.io/api/edge/";

    public void ClearCache() => _genreCache.Clear();

    public JikanApiClient(HttpClient? httpClient = null, HttpClient? kitsuClient = null)
    {
        _isCustomClient = httpClient != null;
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(DefaultBaseUrl),
            Timeout = TimeSpan.FromSeconds(3)
        };

        _kitsuClient = kitsuClient ?? new HttpClient
        {
            BaseAddress = new Uri(DefaultKitsuBaseUrl),
            Timeout = TimeSpan.FromSeconds(6)
        };
        _kitsuClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AnimeTracker/1.0");
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

        // The public Jikan API (api.jikan.moe) was permanently discontinued.
        // If not using a custom client (e.g. self-hosted docker instance or unit test handler),
        // or if Jikan is detected as offline, directly use the fast Kitsu catalog.
        if (!_isCustomClient || _jikanOffline)
        {
            return await SearchKitsuAsync(malType, query, cancellationToken);
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
            _jikanOffline = true;
            try
            {
                return await SearchKitsuAsync(malType, query, cancellationToken);
            }
            catch
            {
                throw new JikanApiException($"Network error while calling Jikan API ({malType}): {ex.Message}", innerException: ex);
            }
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
                    results.Add(ParseJikanItem(item, malType));
                }

                return results;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse Jikan API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    public async Task<IReadOnlyList<JikanSearchResult>> SearchKitsuAsync(string malType, string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trimmed = query.Trim();
        var endpoint = string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase) ? "anime" : "manga";
        var relativeUrl = $"{endpoint}?filter[text]={Uri.EscapeDataString(trimmed)}&include=categories";

        HttpResponseMessage response;
        try
        {
            response = await _kitsuClient.GetAsync(relativeUrl, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JikanApiException($"Network error while calling catalog API ({malType}): {ex.Message}", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new JikanApiException($"Catalog API returned error status: {response.StatusCode}", response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<JikanSearchResult>();
                }

                var categoriesById = new Dictionary<string, string>();
                if (root.TryGetProperty("included", out var includedElement) && includedElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var inc in includedElement.EnumerateArray())
                    {
                        if (inc.TryGetProperty("type", out var incType) && incType.GetString() == "categories" &&
                            inc.TryGetProperty("id", out var incId) &&
                            inc.TryGetProperty("attributes", out var incAttr) &&
                            incAttr.TryGetProperty("title", out var incTitle))
                        {
                            var idStr = incId.GetString();
                            var titleStr = incTitle.GetString();
                            if (!string.IsNullOrEmpty(idStr) && !string.IsNullOrEmpty(titleStr))
                            {
                                categoriesById[idStr] = titleStr;
                            }
                        }
                    }
                }

                var results = new List<JikanSearchResult>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    var parsed = ParseKitsuItem(item, malType, categoriesById);
                    if (parsed != null)
                    {
                        results.Add(parsed);
                    }
                }

                return results;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse catalog API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    public async Task<IReadOnlyList<JikanSearchResult>> GetRecommendationsByGenreAsync(
        string malType,
        string genre,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(genre))
        {
            return Array.Empty<JikanSearchResult>();
        }

        var normalizedType = malType?.Trim().ToLowerInvariant() switch
        {
            "manhwa" => "manhwa",
            "anime" => "anime",
            _ => "manga"
        };
        var cacheKey = $"{normalizedType}:{genre.Trim().ToLowerInvariant()}";

        if (_genreCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.Timestamp) < CacheDuration)
        {
            return cached.Items;
        }

        IReadOnlyList<JikanSearchResult> results;

        if (!_isCustomClient || _jikanOffline)
        {
            results = await GetKitsuRecommendationsByGenreAsync(normalizedType, genre.Trim(), cancellationToken);
        }
        else
        {
            results = await GetJikanRecommendationsByGenreAsync(normalizedType, genre.Trim(), cancellationToken);
        }

        _genreCache[cacheKey] = (DateTime.UtcNow, results);
        return results;
    }

    public async Task<IReadOnlyList<JikanSearchResult>> GetPopularAsync(
        string malType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedType = malType?.Trim().ToLowerInvariant() switch
        {
            "manhwa" => "manhwa",
            "anime" => "anime",
            _ => "manga"
        };
        var cacheKey = $"{normalizedType}:__popular__";

        if (_genreCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.Timestamp) < CacheDuration)
        {
            return cached.Items;
        }

        IReadOnlyList<JikanSearchResult> results;

        if (!_isCustomClient || _jikanOffline)
        {
            results = await GetKitsuPopularAsync(normalizedType, cancellationToken);
        }
        else
        {
            results = await GetJikanPopularAsync(normalizedType, cancellationToken);
        }

        _genreCache[cacheKey] = (DateTime.UtcNow, results);
        return results;
    }

    private async Task<IReadOnlyList<JikanSearchResult>> GetJikanRecommendationsByGenreAsync(
        string malType,
        string genre,
        CancellationToken cancellationToken)
    {
        malType = malType ?? "manga";
        var isAnime = string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase);
        var endpoint = isAnime ? "anime" : "manga";
        var typeParam = malType.Trim().ToLowerInvariant() switch
        {
            "manhwa" => "&type=manhwa",
            "manga" => "&type=manga",
            _ => string.Empty
        };

        string relativeUrl;
        if (JikanGenreIds.TryGetValue(genre, out var genreId))
        {
            relativeUrl = $"{endpoint}?genres={genreId}{typeParam}&order_by=score&sort=desc&limit=25";
        }
        else
        {
            relativeUrl = $"{endpoint}?q={Uri.EscapeDataString(genre)}{typeParam}&order_by=score&sort=desc&limit=25";
        }

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
            _jikanOffline = true;
            try
            {
                return await GetKitsuRecommendationsByGenreAsync(malType, genre, cancellationToken);
            }
            catch
            {
                throw new JikanApiException($"Network error while calling Jikan API ({malType}): {ex.Message}", innerException: ex);
            }
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

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<JikanSearchResult>();
                }

                var list = new List<JikanSearchResult>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    list.Add(ParseJikanItem(item, malType));
                }

                return list;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse Jikan API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    private async Task<IReadOnlyList<JikanSearchResult>> GetJikanPopularAsync(
        string malType,
        CancellationToken cancellationToken)
    {
        malType = malType ?? "manga";
        var isAnime = string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase);
        var endpoint = isAnime ? "anime" : "manga";
        var typeParam = malType.Trim().ToLowerInvariant() switch
        {
            "manhwa" => "&type=manhwa",
            "manga" => "&type=manga",
            _ => string.Empty
        };

        var relativeUrl = $"{endpoint}?order_by=popularity&sort=asc{typeParam}&limit=25";

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
            _jikanOffline = true;
            try
            {
                return await GetKitsuPopularAsync(malType, cancellationToken);
            }
            catch
            {
                throw new JikanApiException($"Network error while calling Jikan API ({malType}): {ex.Message}", innerException: ex);
            }
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

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<JikanSearchResult>();
                }

                var list = new List<JikanSearchResult>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    list.Add(ParseJikanItem(item, malType));
                }

                return list;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse Jikan API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    private async Task<IReadOnlyList<JikanSearchResult>> GetKitsuRecommendationsByGenreAsync(
        string malType,
        string genre,
        CancellationToken cancellationToken)
    {
        malType = malType ?? "manga";
        var isAnime = string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase);
        var endpoint = isAnime ? "anime" : "manga";
        var subtypeFilter = malType.Trim().ToLowerInvariant() switch
        {
            "manhwa" => "&filter[subtype]=manhwa",
            "manga" => "&filter[subtype]=manga",
            _ => string.Empty
        };
        var relativeUrl = $"{endpoint}?filter[categories]={Uri.EscapeDataString(genre)}{subtypeFilter}&sort=-averageRating&page[limit]=20&include=categories";

        HttpResponseMessage response;
        try
        {
            response = await _kitsuClient.GetAsync(relativeUrl, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JikanApiException($"Network error while calling catalog API ({malType}): {ex.Message}", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new JikanApiException($"Catalog API returned error status: {response.StatusCode}", response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<JikanSearchResult>();
                }

                var categoriesById = new Dictionary<string, string>();
                if (root.TryGetProperty("included", out var includedElement) && includedElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var inc in includedElement.EnumerateArray())
                    {
                        if (inc.TryGetProperty("type", out var incType) && incType.GetString() == "categories" &&
                            inc.TryGetProperty("id", out var incId) &&
                            inc.TryGetProperty("attributes", out var incAttr) &&
                            incAttr.TryGetProperty("title", out var incTitle))
                        {
                            var idStr = incId.GetString();
                            var titleStr = incTitle.GetString();
                            if (!string.IsNullOrEmpty(idStr) && !string.IsNullOrEmpty(titleStr))
                            {
                                categoriesById[idStr] = titleStr;
                            }
                        }
                    }
                }

                var list = new List<JikanSearchResult>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    var parsed = ParseKitsuItem(item, malType, categoriesById);
                    if (parsed != null)
                    {
                        list.Add(parsed);
                    }
                }

                return list;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse catalog API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    private async Task<IReadOnlyList<JikanSearchResult>> GetKitsuPopularAsync(
        string malType,
        CancellationToken cancellationToken)
    {
        malType = malType ?? "manga";
        var isAnime = string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase);
        var endpoint = isAnime ? "anime" : "manga";
        var subtypeFilter = malType.Trim().ToLowerInvariant() switch
        {
            "manhwa" => "&filter[subtype]=manhwa",
            "manga" => "&filter[subtype]=manga",
            _ => string.Empty
        };
        var relativeUrl = $"{endpoint}?sort=-userCount{subtypeFilter}&page[limit]=20&include=categories";

        HttpResponseMessage response;
        try
        {
            response = await _kitsuClient.GetAsync(relativeUrl, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JikanApiException($"Network error while calling catalog API ({malType}): {ex.Message}", innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new JikanApiException($"Catalog API returned error status: {response.StatusCode}", response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<JikanSearchResult>();
                }

                var categoriesById = new Dictionary<string, string>();
                if (root.TryGetProperty("included", out var includedElement) && includedElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var inc in includedElement.EnumerateArray())
                    {
                        if (inc.TryGetProperty("type", out var incType) && incType.GetString() == "categories" &&
                            inc.TryGetProperty("id", out var incId) &&
                            inc.TryGetProperty("attributes", out var incAttr) &&
                            incAttr.TryGetProperty("title", out var incTitle))
                        {
                            var idStr = incId.GetString();
                            var titleStr = incTitle.GetString();
                            if (!string.IsNullOrEmpty(idStr) && !string.IsNullOrEmpty(titleStr))
                            {
                                categoriesById[idStr] = titleStr;
                            }
                        }
                    }
                }

                var list = new List<JikanSearchResult>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    var parsed = ParseKitsuItem(item, malType, categoriesById);
                    if (parsed != null)
                    {
                        list.Add(parsed);
                    }
                }

                return list;
            }
            catch (JsonException ex)
            {
                throw new JikanApiException($"Failed to parse catalog API response JSON: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    private static JikanSearchResult ParseJikanItem(JsonElement item, string malType)
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
            if (item.TryGetProperty("episodes", out var epProp) && epProp.ValueKind == JsonValueKind.Number && epProp.TryGetInt32(out var eps))
            {
                totalCount = eps;
            }
        }
        else
        {
            if (item.TryGetProperty("chapters", out var chProp) && chProp.ValueKind == JsonValueKind.Number && chProp.TryGetInt32(out var chs))
            {
                totalCount = chs;
            }
        }

        var synopsis = GetStringOrNull(item, "synopsis");

        double? score = null;
        if (item.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number && scoreProp.TryGetDouble(out var sVal))
        {
            score = sVal;
        }

        int? year = null;
        if (item.TryGetProperty("year", out var yearProp) && yearProp.ValueKind == JsonValueKind.Number && yearProp.TryGetInt32(out var yVal))
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

    private static JikanSearchResult? ParseKitsuItem(JsonElement item, string malType, Dictionary<string, string> categoriesById)
    {
        if (!item.TryGetProperty("id", out var idProp)) return null;
        var idStr = idProp.GetString();
        if (!int.TryParse(idStr, out var idVal))
        {
            idVal = Math.Abs(idStr?.GetHashCode() ?? 0);
        }

        if (!item.TryGetProperty("attributes", out var attr) || attr.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var canonicalTitle = GetStringOrNull(attr, "canonicalTitle");
        string? englishTitle = null;
        if (attr.TryGetProperty("titles", out var titlesObj) && titlesObj.ValueKind == JsonValueKind.Object)
        {
            englishTitle = GetStringOrNull(titlesObj, "en")
                ?? GetStringOrNull(titlesObj, "en_us")
                ?? GetStringOrNull(titlesObj, "en_jp");
        }

        var title = !string.IsNullOrWhiteSpace(canonicalTitle)
            ? canonicalTitle
            : englishTitle ?? "Unknown Title";

        string? coverUrl = null;
        if (attr.TryGetProperty("posterImage", out var posterObj) && posterObj.ValueKind == JsonValueKind.Object)
        {
            coverUrl = GetStringOrNull(posterObj, "large")
                ?? GetStringOrNull(posterObj, "original")
                ?? GetStringOrNull(posterObj, "medium")
                ?? GetStringOrNull(posterObj, "small")
                ?? GetStringOrNull(posterObj, "tiny");
        }

        int? totalCount = null;
        if (string.Equals(malType, "anime", StringComparison.OrdinalIgnoreCase))
        {
            if (attr.TryGetProperty("episodeCount", out var epProp) && epProp.ValueKind == JsonValueKind.Number && epProp.TryGetInt32(out var eps))
            {
                totalCount = eps;
            }
        }
        else
        {
            if (attr.TryGetProperty("chapterCount", out var chProp) && chProp.ValueKind == JsonValueKind.Number && chProp.TryGetInt32(out var chs))
            {
                totalCount = chs;
            }
        }

        var synopsis = GetStringOrNull(attr, "synopsis") ?? GetStringOrNull(attr, "description");

        double? score = null;
        var ratingStr = GetStringOrNull(attr, "averageRating");
        if (!string.IsNullOrEmpty(ratingStr) && double.TryParse(ratingStr, System.Globalization.CultureInfo.InvariantCulture, out var ratingVal))
        {
            score = Math.Round(ratingVal / 10.0, 2);
        }

        int? year = null;
        var startDate = GetStringOrNull(attr, "startDate");
        if (!string.IsNullOrEmpty(startDate) && startDate.Length >= 4 && int.TryParse(startDate.Substring(0, 4), out var yVal))
        {
            year = yVal;
        }

        var genresList = new List<string>();
        if (item.TryGetProperty("relationships", out var relObj) && relObj.ValueKind == JsonValueKind.Object &&
            relObj.TryGetProperty("categories", out var catObj) && catObj.ValueKind == JsonValueKind.Object &&
            catObj.TryGetProperty("data", out var catData) && catData.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in catData.EnumerateArray())
            {
                var cId = GetStringOrNull(c, "id");
                if (cId != null && categoriesById.TryGetValue(cId, out var gTitle) && !genresList.Contains(gTitle))
                {
                    genresList.Add(gTitle);
                }
            }
        }

        return new JikanSearchResult
        {
            MalId = idVal,
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
