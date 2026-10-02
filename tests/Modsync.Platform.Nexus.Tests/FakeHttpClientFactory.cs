namespace Modsync.Platform.Nexus.Tests;

/// <summary>
/// Fake IHttpClientFactory: по имени возвращает HttpClient, обёрнутый
/// вокруг заранее зарегистрированного HttpMessageHandler.
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpClient> _clients =
        new(StringComparer.Ordinal);

    public HttpClient CreateClient(string name)
    {
        if (!_clients.TryGetValue(name, out var client))
            throw new InvalidOperationException(
                $"No fake client registered for name '{name}'.");
        return client;
    }

    public void Register(string name, HttpMessageHandler handler)
    {
        _clients[name] = new HttpClient(handler);
    }
}
