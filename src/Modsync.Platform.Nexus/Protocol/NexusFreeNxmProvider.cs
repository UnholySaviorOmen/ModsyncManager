// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Modsync.Platform.Nexus.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Реализация INexusFreeNxmProvider без WebView2.
///
/// Логика:
///   1. Проверить, что наш handler зарегистрирован в HKCU.
///      Если нет — бросить InvalidOperationException с понятным
///      сообщением. Install прерывается, пользователь идёт в Settings.
///   2. Открыть страницу мода в браузере пользователя
///      (https://www.nexusmods.com/{game}/mods/{modId}?tab=files&file_id={fileId}&nmm=1).
///   3. Ждать nxm:// URL от INxmUrlReceiver (handler
///      ModsyncManager.NxmHandler.exe → named pipe → NxmUrlReceiver).
///   4. Вернуть полученный URL.
///
/// Сериализация: SemaphoreSlim(1,1). Один вызов RequestNxmUrlAsync
/// за раз. Это гарантирует, что URL, пришедший от handler-а, точно
/// относится к текущему запросу (в очереди Channel-а URL-ы лежат
/// FIFO, но «кто первый встал — того и тапки» для нескольких
/// параллельных WaitForUrlAsync).
///
/// Очистка очереди: перед началом ожидания — TryDrainPendingUrls.
/// URL-ы от прошлых сессий (пользователь отменил install, URL уже
/// пришёл) не должны попасть в текущий install. Иначе
/// NexusDownloader сверит game/modId/fileId и упадёт с
/// InvalidOperationException.
///
/// Timeout: 5 минут. Пользователь может кликнуть «Mod Manager Download»
/// в браузере за это время. Если не кликнул — возвращаем null.
///
/// Не помечен [SupportedOSPlatform("windows")]: IUrlOpener работает
/// на всех платформах, а INxmUrlReceiver — кросс-платформенный.
/// Реально на Linux сюда никто не постучится (нет handler-а).
/// </summary>
public sealed class NexusFreeNxmProvider : INexusFreeNxmProvider
{
    /// <summary>
    /// Таймаут ожидания URL от handler-а.
    /// </summary>
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(5);

    private readonly INxmUrlReceiver _receiver;
    private readonly IUrlOpener _urlOpener;
    private readonly IProtocolRegistrar _registrar;
    private readonly ILogger<NexusFreeNxmProvider> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public NexusFreeNxmProvider(
        INxmUrlReceiver receiver,
        IUrlOpener urlOpener,
        IProtocolRegistrar registrar,
        ILogger<NexusFreeNxmProvider> logger)
    {
        _receiver = receiver;
        _urlOpener = urlOpener;
        _registrar = registrar;
        _logger = logger;
    }

    public async Task<string?> RequestNxmUrlAsync(
        string game,
        int modId,
        int fileId,
        string displayName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(game))
            throw new ArgumentException(
                "Game must be non-empty.", nameof(game));
        if (modId <= 0)
            throw new ArgumentOutOfRangeException(nameof(modId));
        if (fileId <= 0)
            throw new ArgumentOutOfRangeException(nameof(fileId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException(
                "Display name must be non-empty.", nameof(displayName));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RequestCoreAsync(
                game, modId, fileId, displayName, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------
    //  Core logic
    // ------------------------------------------------------------------

    private async Task<string?> RequestCoreAsync(
        string game,
        int modId,
        int fileId,
        string displayName,
        CancellationToken ct)
    {
        // 1. Проверяем, что handler зарегистрирован на нас.
        //    Если нет — install прерывается. Пользователь должен
        //    сначала включить Free Download в Settings.
        var state = _registrar.GetState();
        if (state != ProtocolRegistrationState.RegisteredToUs)
        {
            throw new InvalidOperationException(
                $"nxm:// handler is not registered to ModsyncManager " +
                $"(current state: {state}). " +
                $"Enable it in Settings → Nexus Free Download, " +
                $"then retry the install.");
        }

        // 2. Чистим очередь от «протухших» URL предыдущих сессий.
        //    URL-ы от прошлых install (пользователь отменил, URL уже
        //    пришёл) не должны попасть в текущий install. Иначе
        //    NexusDownloader сверит game/modId/fileId и упадёт с
        //    InvalidOperationException.
        var drained = _receiver.TryDrainPendingUrls();

        if (drained > 0)
        {
            _logger.LogInformation(
                "Drained {Count} stale nxm:// URL(s) from a previous " +
                "install session before requesting a new one.",
                drained);
        }

        // 3. Открываем страницу мода в браузере.
        var modUrl = BuildModUrl(game, modId, fileId);

        _logger.LogInformation(
            "Opening browser for {DisplayName}: {Url}",
            displayName, modUrl);

        try
        {
            _urlOpener.Open(modUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to open browser for {DisplayName}.",
                displayName);
            throw;
        }

        // 4. Ждём URL от receiver-а с таймаутом.
        _logger.LogInformation(
            "Waiting for nxm:// URL for {DisplayName} (timeout: {Timeout}). " +
            "In the browser, click \"Mod Manager Download\" " +
            "(not Slow Download) on the mod page.",
            displayName, WaitTimeout);

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(WaitTimeout);

        string? url;
        try
        {
            url = await _receiver.WaitForUrlAsync(timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested)
        {
            // Timeout сработал, но внешний ct не отменён.
            _logger.LogWarning(
                "Timed out waiting for nxm:// URL for {DisplayName} " +
                "after {Timeout}.",
                displayName, WaitTimeout);
            return null;
        }
        // Операция отменена внешним ct — пробрасываем дальше.
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Cancelled while waiting for nxm:// URL for {DisplayName}.",
                displayName);
            throw;
        }

        if (url is null)
        {
            // Receiver остановлен (StopAsync) — Channel закрыт.
            _logger.LogWarning(
                "nxm URL receiver stopped before URL arrived " +
                "for {DisplayName}.",
                displayName);
            return null;
        }

        // 5. Логируем URL без query-параметров (там key — временный токен).
        _logger.LogInformation(
            "Received nxm:// URL for {DisplayName}: {SafeUrl}",
            displayName, SanitizeUrl(url));

        return url;
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static string BuildModUrl(string game, int modId, int fileId)
        => $"https://www.nexusmods.com/{game}/mods/{modId}" +
           $"?tab=files&file_id={fileId}&nmm=1";

    /// <summary>
    /// Убирает query-параметры из nxm:// URL для безопасного
    /// логирования. key/expires/user_id — чувствительные данные.
    /// </summary>
    internal static string SanitizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var questionMark = url.IndexOf('?');
        return questionMark >= 0 ? url[..questionMark] : url;
    }
}
