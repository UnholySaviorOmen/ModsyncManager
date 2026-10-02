// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus;

/// <summary>
/// Провайдер nxm://-URL для Free-скачивания с Nexus.
///
/// Проблема: Nexus не даёт прямых download-ссылок через API для
/// Free-аккаунтов. Единственный способ — nxm://, который Nexus
/// генерирует при клике «Mod Manager Download» на сайте, с временными
/// параметрами key/expires.
///
/// Реализация (NexusFreeNxmProvider) открывает страницу мода
/// в браузере пользователя, ждёт nxm:// от ModsyncManager.NxmHandler.exe
/// (через named pipe) и отдаёт URL в pipeline.
///
/// Default-реализация (NullNexusFreeNxmProvider) бросает
/// InvalidOperationException. Используется там, где нет UI.
///
/// Сериализация запросов — ответственность реализации. Даже если
/// несколько DownloadAsync вызовут провайдер параллельно (например,
/// через Parallel.ForEachAsync), реализация обязана выполнять их
/// последовательно (один пользователь — один браузер — один nxm).
/// </summary>
public interface INexusFreeNxmProvider
{
    /// <summary>
    /// Запрашивает у пользователя nxm://-URL для указанного файла.
    ///
    /// Возвращает валидный nxm:// URL, или null, если пользователь
    /// не кликнул / отменил действие. В случае null NexusDownloader
    /// бросит InvalidOperationException — installer упадёт, пользователь
    /// перезапустит (installer идемпотентен).
    ///
    /// Бросает OperationCanceledException, если ct отменён.
    /// Бросает другие исключения при внутренних ошибках.
    /// </summary>
    /// <param name="game">Nexus game domain.</param>
    /// <param name="modId">ID мода.</param>
    /// <param name="fileId">ID файла.</param>
    /// <param name="displayName">
    /// Имя архива из манифеста — для логов и UI («Скачивание: SkyUI.7z»).
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    Task<string?> RequestNxmUrlAsync(
        string game,
        int modId,
        int fileId,
        string displayName,
        CancellationToken ct);
}
