using System;
using System.Net;

namespace AnimeTracker.Services.Jikan;

public class JikanApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    public JikanApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
