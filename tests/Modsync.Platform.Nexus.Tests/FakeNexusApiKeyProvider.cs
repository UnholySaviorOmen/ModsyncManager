using Modsync.Platform.Nexus;

namespace Modsync.Platform.Nexus.Tests;

/// <summary>
/// Fake-реализация INexusApiKeyProvider для тестов NexusClient.
///
/// Хранит ключ в памяти. Save/Clear изменяют Key — этого хватает
/// для тестов, где нужно проверить поведение «ключ есть / нет / заменился».
/// </summary>
public sealed class FakeNexusApiKeyProvider : INexusApiKeyProvider
{
    public string? Key { get; set; }

    public string KeyFilePath => "<fake>";

    public FakeNexusApiKeyProvider(string? key = null)
    {
        Key = key;
    }

    public string? TryGetApiKey() => Key;

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "API key must be non-empty.", nameof(apiKey));

        Key = apiKey.Trim();
    }

    public void Clear()
    {
        Key = null;
    }
}
