using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AnimeTracker.Services.Jikan;
using Xunit;

namespace AnimeTracker.Tests;

public class JikanApiClientTests
{
    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public int RequestCount { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            LastRequest = request;

            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        }
    }

    [Fact]
    public async Task SearchAnimeAsync_CallsAnimeEndpoint_WithEncodedQuery()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        await client.SearchAnimeAsync("Frieren & Beyond");

        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Contains("anime?q=Frieren%20%26%20Beyond", handler.LastRequest.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task SearchMangaAsync_CallsMangaEndpoint_WithEncodedQuery()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        await client.SearchMangaAsync("Chainsaw Man");

        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Contains("manga?q=Chainsaw%20Man", handler.LastRequest.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task SearchAsync_MapsAllExpectedDtoFieldsCorrectly()
    {
        var jsonResponse = """
        {
            "data": [
                {
                    "mal_id": 52991,
                    "title": "Sousou no Frieren",
                    "title_english": "Frieren: Beyond Journey's End",
                    "images": {
                        "webp": {
                            "large_image_url": "https://cdn.myanimelist.net/images/anime/1015/138006l.webp",
                            "image_url": "https://cdn.myanimelist.net/images/anime/1015/138006.webp"
                        }
                    },
                    "episodes": 28,
                    "synopsis": "An elf mage journeys forward.",
                    "score": 9.32,
                    "year": 2023,
                    "genres": [
                        { "mal_id": 1, "name": "Adventure" },
                        { "mal_id": 2, "name": "Fantasy" }
                    ]
                }
            ]
        }
        """;

        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var results = await client.SearchAnimeAsync("Frieren");

        Assert.Single(results);
        var item = results[0];
        Assert.Equal(52991, item.MalId);
        Assert.Equal("anime", item.MalType);
        Assert.Equal("Sousou no Frieren", item.Title);
        Assert.Equal("Frieren: Beyond Journey's End", item.EnglishTitle);
        Assert.Equal("https://cdn.myanimelist.net/images/anime/1015/138006l.webp", item.CoverImageUrl);
        Assert.Equal(28, item.TotalCount);
        Assert.Equal("An elf mage journeys forward.", item.Synopsis);
        Assert.Equal(9.32, item.Score);
        Assert.Equal(2023, item.Year);
        Assert.Equal(new[] { "Adventure", "Fantasy" }, item.Genres);
    }

    [Fact]
    public async Task SearchMangaAsync_MapsChaptersToTotalCount()
    {
        var jsonResponse = """
        {
            "data": [
                {
                    "mal_id": 121496,
                    "title": "Solo Leveling",
                    "title_english": null,
                    "chapters": 179,
                    "genres": []
                }
            ]
        }
        """;

        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var results = await client.SearchMangaAsync("Solo Leveling");

        Assert.Single(results);
        var item = results[0];
        Assert.Equal(121496, item.MalId);
        Assert.Equal("manga", item.MalType);
        Assert.Equal("Solo Leveling", item.Title);
        Assert.Null(item.EnglishTitle);
        Assert.Equal(179, item.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_HandlesMissingAndNullFieldsSafely()
    {
        var jsonResponse = """
        {
            "data": [
                {
                    "mal_id": 999,
                    "title": "Minimal Entry"
                }
            ]
        }
        """;

        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var results = await client.SearchAnimeAsync("Minimal");

        Assert.Single(results);
        var item = results[0];
        Assert.Equal(999, item.MalId);
        Assert.Equal("anime", item.MalType);
        Assert.Equal("Minimal Entry", item.Title);
        Assert.Null(item.EnglishTitle);
        Assert.Null(item.CoverImageUrl);
        Assert.Null(item.TotalCount);
        Assert.Null(item.Synopsis);
        Assert.Null(item.Score);
        Assert.Null(item.Year);
        Assert.Empty(item.Genres);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_WithEmptyOrWhitespaceQuery_ReturnsEmptyList_WithoutSendingRequest(string? query)
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var results = await client.SearchAnimeAsync(query!);

        Assert.Empty(results);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_PassesCancellationToken()
    {
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SearchAnimeAsync("Cancelled", cancellationTokenSource.Token));
    }

    [Fact]
    public async Task SearchAsync_WhenHttp429TooManyRequests_ThrowsJikanApiExceptionWithRateLimit()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<JikanApiException>(() => client.SearchAnimeAsync("Naruto"));
        Assert.True(ex.IsRateLimited);
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
    }

    [Fact]
    public async Task SearchAsync_WhenHttp500InternalServerError_ThrowsJikanApiException()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<JikanApiException>(() => client.SearchAnimeAsync("Naruto"));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.False(ex.IsRateLimited);
    }

    [Fact]
    public async Task SearchAsync_WhenJsonIsInvalid_ThrowsJikanApiException()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("This is not valid json")
            }
        };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.jikan.moe/v4/") };
        var client = new JikanApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<JikanApiException>(() => client.SearchAnimeAsync("Naruto"));
        Assert.NotNull(ex.InnerException);
    }
}
