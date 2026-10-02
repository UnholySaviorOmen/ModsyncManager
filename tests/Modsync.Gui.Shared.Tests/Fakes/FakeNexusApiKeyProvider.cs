using Modsync.Platform.Nexus;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeNexusApiKeyProvider : INexusApiKeyProvider
{
    public string? Key { get; set; }
    public string KeyFilePath => "<fake>";

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
