// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Core.Abstractions;

/// <summary>
/// Скачивание одного архива по конкретному источнику.
///
/// Каждый источник (github, mirror, nexus) — отдельная реализация.
/// SyncArchivesStep перебирает реализации по типу source-а.
///
/// Возвращает Stream с содержимым архива. Ответственность за закрытие
/// stream-а — на вызывающем коде. Ответственность за проверку хеша —
/// тоже на вызывающем коде (SyncArchivesStep): downloader не знает,
/// какой хеш ожидается в манифесте.
/// </summary>
public interface IArchiveDownloader
{
    /// <summary>
    /// Тип источника, который умеет обрабатывать этот downloader.
    /// Соответствует значению из json-дискриминатора (например, "github").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Скачать содержимое архива по источнику.
    ///
    /// Бросает исключение при неудаче. Retry — на стороне
    /// ArchiveDownloadHelper. Если исключение — «постоянная»
    /// ошибка (см. IsPermanentFailure), retry пропускается.
    /// </summary>
    Task<Stream> DownloadAsync(
        ArchiveSourceRef source,
        CancellationToken ct);

    /// <summary>
    /// Является ли исключение «постоянной» ошибкой, на которую
    /// бессмысленно повторять попытку.
    ///
    /// Пример: NexusAuthenticationException — ключ не появится от
    /// повторной попытки. Retry только тратит время.
    ///
    /// Default: false (все ошибки retry-абельны).
    /// </summary>
    bool IsPermanentFailure(Exception ex) => false;
}
