// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Platform.Nexus;

namespace Modsync.Integration.Tests;

/// <summary>
/// Заглушка INexusApiKeyProvider для integration-тестов.
///
/// Возвращает фиктивный ключ (непустой), чтобы
/// PreflightNexusAuthStep не падал на манифестах с nexus-источниками.
/// Реальные HTTP-запросы в тестах не делаются — downloaders в
/// integration-тестах фейковые (в downloads/ уже лежат архивы).
/// </summary>
internal sealed class NexusApiKeyProviderStub : INexusApiKeyProvider
{
    private string? _key;

    public NexusApiKeyProviderStub(string? key = "test-key")
    {
        _key = key;
    }

    public string KeyFilePath => "<stub>";

    public string? TryGetApiKey() => _key;

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "API key must be non-empty.", nameof(apiKey));

        _key = apiKey.Trim();
    }

    public void Clear()
    {
        _key = null;
    }
}
