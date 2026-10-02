// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus;

/// <summary>
/// Никогда не возвращает ключ и ничего не сохраняет.
///
/// Полезен как fallback для DI-регистрации, а также в тестах,
/// где проверяется поведение «ключа нет».
/// </summary>
public sealed class NullNexusApiKeyProvider : INexusApiKeyProvider
{
    public string KeyFilePath => string.Empty;

    public string? TryGetApiKey() => null;

    public void Save(string apiKey)
    {
        throw new NotSupportedException(
            "NullNexusApiKeyProvider does not store credentials.");
    }

    public void Clear()
    {
        // Ничего не делаем — нечего удалять.
    }
}
