// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus;

/// <summary>
/// Скачивание архивов с Nexus Mods через официальный API.
///
/// Две стратегии доступа:
///
///   Premium (аккаунт Premium):
///     GET /games/{game}/mods/{modId}/files/{fileId}/download_link.json
///     Заголовок apikey. Список CDN-нод, качаем первую рабочую.
///
///   Free (аккаунт Free):
///     1. INexusFreeNxmProvider открывает браузер, пользователь
///        кликает «Mod Manager Download», провайдер отдаёт nxm://URL.
///     2. Сверяем user_id из nxm:// с user_id из /users/validate.json.
///     3. GET /games/{game}/mods/{modId}/files/{fileId}/download_link.json
///        ?key={key}&expires={expires}
///        Заголовок apikey. Список CDN-нод, качаем первую рабочую.
///
/// HttpClient для CDN берётся из IHttpClientFactory по имени "nexus"
/// (таймаут 10 минут). Это критично для больших архивов — у Nexus
/// есть моды на 3+ ГБ.
///
/// Скачивание идёт в TempFileStream (временный файл на диске), а не в
/// MemoryStream: MemoryStream не держит больше ~2 ГБ.
///
/// Retry / hash-check / .part — на стороне ArchiveDownloadHelper.
/// </summary>
public sealed class NexusDownloader : IArchiveDownloader
{
    public const string HttpClientName = "nexus";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NexusClient _client;
    private readonly INexusFreeNxmProvider _nxmProvider;
    private readonly ILogger<NexusDownloader> _logger;

    public NexusDownloader(
        IHttpClientFactory httpClientFactory,
        NexusClient client,
        INexusFreeNxmProvider nxmProvider,
        ILogger<NexusDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _client = client;
        _nxmProvider = nxmProvider;
        _logger = logger;
    }

    public string SourceType => "nexus";

    public async Task<Stream> DownloadAsync(
        ArchiveSourceRef source, CancellationToken ct)
    {
        if (source is not NexusSourceRef nexus)
        {
            throw new ArgumentException(
                $"Expected NexusSourceRef, got {source.GetType().Name}.",
                nameof(source));
        }

        ct.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Downloading from Nexus: {Game}/{ModId}/{FileId}",
            nexus.Game, nexus.ModId, nexus.FileId);

        // 1. Узнаём про аккаунт. Кешируется в NexusClient — один
        // HTTP-запрос на весь pipeline.
        var validateInfo = await _client.GetValidateInfoAsync(ct);

        ct.ThrowIfCancellationRequested();

        IReadOnlyList<Models.NexusDownloadLink> links;

        if (validateInfo.IsPremium)
        {
            // Premium-ветка: обычный download_link без параметров.
            links = await GetPremiumLinksAsync(nexus, ct);
        }
        else
        {
            // Free-ветка: получаем nxm:// через провайдер, парсим,
            // сверяем user_id, запрашиваем download_link с key/expires.
            links = await GetFreeLinksAsync(
                nexus, validateInfo.UserId, ct);
        }

        if (links.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nexus returned no download links for " +
                $"{nexus.Game}/{nexus.ModId}/{nexus.FileId}. " +
                "The file may be hidden, archived, or restricted.");
        }

        _logger.LogDebug(
            "Nexus returned {Count} CDN node(s) for {Game}/{ModId}/{FileId}",
            links.Count, nexus.Game, nexus.ModId, nexus.FileId);

        // 2. Перебираем CDN-ноды по очереди.
        return await DownloadFromFirstWorkingCdnAsync(
            nexus, links, ct);
    }

    // ------------------------------------------------------------------
    //  Premium
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<Models.NexusDownloadLink>>
        GetPremiumLinksAsync(NexusSourceRef nexus, CancellationToken ct)
    {
        return await _client.GetDownloadLinksAsync(
            nexus.Game, nexus.ModId, nexus.FileId,
            ct: ct);
    }

    // ------------------------------------------------------------------
    //  Free
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<Models.NexusDownloadLink>>
        GetFreeLinksAsync(
            NexusSourceRef nexus,
            int currentUserId,
            CancellationToken ct)
    {
        _logger.LogInformation(
            "Nexus account is not Premium. Requesting nxm:// for " +
            "{Game}/{ModId}/{FileId} via INexusFreeNxmProvider.",
            nexus.Game, nexus.ModId, nexus.FileId);

        var displayName = $"{nexus.Game}/{nexus.ModId}/{nexus.FileId}";
        var nxmUrl = await _nxmProvider.RequestNxmUrlAsync(
            nexus.Game, nexus.ModId, nexus.FileId,
            displayName, ct);

        ct.ThrowIfCancellationRequested();

        if (nxmUrl is null)
        {
            throw new InvalidOperationException(
                $"Download of {nexus.Game}/{nexus.ModId}/{nexus.FileId} " +
                "was cancelled by the user. Restart the installation to " +
                "continue — already downloaded archives will be skipped.");
        }

        if (!NexusUrlParser.TryParse(nxmUrl, out var parsed) || parsed is null)
        {
            throw new InvalidOperationException(
                $"INexusFreeNxmProvider returned an invalid nxm:// URL: " +
                $"'{nxmUrl}'. This is a bug — please report.");
        }

        // Сверяем game/modId/fileId с тем, что мы запрашивали. Провайдер
        // мог вернуть URL для другого файла — это ошибка провайдера.
        if (!string.Equals(parsed.Game, nexus.Game, StringComparison.Ordinal)
            || parsed.ModId != nexus.ModId
            || parsed.FileId != nexus.FileId)
        {
            throw new InvalidOperationException(
                $"INexusFreeNxmProvider returned a URL for a different file: " +
                $"expected {nexus.Game}/{nexus.ModId}/{nexus.FileId}, " +
                $"got {parsed.Game}/{parsed.ModId}/{parsed.FileId}. " +
                "This is a bug — please report.");
        }

        if (!parsed.HasFreeCredentials)
        {
            throw new InvalidOperationException(
                "Nexus did not generate temporary credentials (key/expires) " +
                $"for {nexus.Game}/{nexus.ModId}/{nexus.FileId}. " +
                "Make sure you are logged in on the Nexus page in the " +
                "browser before clicking «Mod Manager Download».");
        }

        // Сверяем user_id из nxm:// с user_id нашего API-ключа. Если они
        // разные — Nexus вернёт 403 на download_link?key=... Мы ловим
        // это явно, чтобы дать понятное сообщение.
        if (parsed.UserId != currentUserId)
        {
            throw new InvalidOperationException(
                $"Your Nexus API key belongs to user {currentUserId}, " +
                $"but the nxm:// link was generated for user {parsed.UserId}. " +
                "Log in with the same account on the Nexus page and with " +
                "the same API key.");
        }

        _logger.LogDebug(
            "Free download: {Game}/{ModId}/{FileId}, expires={Expires}, " +
            "userId={UserId}",
            nexus.Game, nexus.ModId, nexus.FileId,
            parsed.Expires, parsed.UserId);

        return await _client.GetDownloadLinksAsync(
            nexus.Game, nexus.ModId, nexus.FileId,
            nxmKey: parsed.Key,
            nxmExpires: parsed.Expires,
            ct: ct);
    }

    // ------------------------------------------------------------------
    //  CDN
    // ------------------------------------------------------------------

    private async Task<Stream> DownloadFromFirstWorkingCdnAsync(
        NexusSourceRef nexus,
        IReadOnlyList<Models.NexusDownloadLink> links,
        CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        Exception? lastError = null;

        for (int i = 0; i < links.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var link = links[i];
            var label = string.IsNullOrWhiteSpace(link.Name)
                ? $"#{i + 1}"
                : link.Name;

            try
            {
                var stream = await DownloadFromCdnAsync(http, link.Uri!, ct);

                _logger.LogDebug(
                    "Nexus CDN node {Label} succeeded for {Game}/{ModId}/{FileId}",
                    label, nexus.Game, nexus.ModId, nexus.FileId);

                return stream;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;

                _logger.LogWarning(ex,
                    "Nexus CDN node {Label} failed for {Game}/{ModId}/{FileId}: {Message}",
                    label, nexus.Game, nexus.ModId, nexus.FileId, ex.Message);
            }
        }

        throw new InvalidOperationException(
            $"All {links.Count} Nexus CDN nodes failed for " +
            $"{nexus.Game}/{nexus.ModId}/{nexus.FileId}: " +
            $"{lastError?.Message ?? "unknown error"}",
            lastError);
    }

    /// <summary>
    /// Скачивает одну CDN-ноду во временный файл. Возвращает TempFileStream
    /// с Position=0.
    ///
    /// Hash не проверяем — это делает ArchiveDownloadHelper по archive.Hash.
    /// Пишем не в MemoryStream, а в TempFileStream: архивы бывают > 2 ГБ.
    /// </summary>
    private static async Task<Stream> DownloadFromCdnAsync(
        HttpClient http, Uri url, CancellationToken ct)
    {
        using var response = await http.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Nexus CDN returned {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}) for {url.Host}.");
        }

        var temp = new TempFileStream();

        try
        {
            await using (var networkStream = await response.Content
                .ReadAsStreamAsync(ct))
            {
                await networkStream.CopyToAsync(temp, ct);
            }

            temp.Position = 0;
            return temp;
        }
        catch
        {
            await temp.DisposeAsync();
            throw;
        }
    }
}
