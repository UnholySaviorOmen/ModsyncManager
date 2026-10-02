// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus;

/// <summary>
/// Источник Nexus API-ключа.
///
/// Абстракция нужна, чтобы NexusClient и NexusDownloader тестировались
/// без реального файла в %LOCALAPPDATA%\ModsyncManager\nexus.key.
/// </summary>
public interface INexusApiKeyProvider
{
    /// <summary>
    /// Возвращает API-ключ или null, если ключа нет / файл недоступен / файл пуст.
    /// Никогда не бросает исключение.
    /// </summary>
    string? TryGetApiKey();

    /// <summary>
    /// Сохраняет ключ. Создаёт папку, если её нет.
    /// Перезаписывает существующий файл.
    ///
    /// Бросает ArgumentException, если ключ пустой или whitespace.
    /// Бросает IOException или UnauthorizedAccessException при ошибке записи.
    /// </summary>
    void Save(string apiKey);

    /// <summary>
    /// Удаляет сохранённый ключ. Не бросает, если файла нет.
    /// Бросает IOException при ошибке удаления.
    /// </summary>
    void Clear();

    /// <summary>
    /// Путь к файлу с ключом. Для диагностики и тестов.
    /// </summary>
    string KeyFilePath { get; }
}
