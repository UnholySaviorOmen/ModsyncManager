using System.Net;
using System.Net.Http;

namespace Modsync.Platform.Nexus.Tests;

/// <summary>
/// Fake HttpMessageHandler: каждый вызов SendAsync отвечает заданной
/// функцией. Запоминает все полученные запросы для проверки заголовков.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public FakeHttpMessageHandler(HttpStatusCode status, string? body = null, string contentType = "application/json")
        : this(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body ?? "", System.Text.Encoding.UTF8, contentType),
        })
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
