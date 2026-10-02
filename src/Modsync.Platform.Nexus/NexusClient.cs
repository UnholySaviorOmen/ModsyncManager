// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http.Json;
using Modsync.Platform.Nexus.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus;

/// <summary>
/// HTTP-клиент к Nexus API (https://api.nexusmods.com/v1/).
///
/// Заголовки на каждый запрос:
///   apikey: &lt;ключ&gt;
///   Application-Name: ModsyncManager
///   Application-Version: 0.1.0
///   User-Agent: ModsyncManager/0.1.0
///   Accept: application/json
///
/// Обработка ошибок: 401 → NexusAuthenticationException.
/// 403/404/429 → InvalidOperationException с внятным текстом.
/// Остальные не-2xx → HttpRequestException.
///
/// Кеш /users/validate.json:
/// Результат первого успешного запроса запоминается на всё время жизни
/// инстанса (NexusClient — синглтон в DI). Параллельные вызовы получают
/// одну и ту же Task — один HTTP-запрос. Faulted/canceled результат
/// НЕ кешируется: retry должен переспросить.
///
/// НЕ делает retry download-ссылок — это ответственность
/// ArchiveDownloadHelper.
/// НЕ кеширует download-ссылки — они временные.
/// </summary>
public sealed class NexusClient
{
    private const string BaseUrl = "https://api.nexusmods.com/v1";
    private const string ApplicationName = "ModsyncManager";
    private const string ApplicationVersion = "0.1.0";
    private const string UserAgent = "ModsyncManager/0.1.0";

    private readonly HttpClient _http;
    private readonly INexusApiKeyProvider _keyProvider;
    private readonly ILogger<NexusClient> _logger;

    private readonly object _validateLock = new();
    private Task<NexusValidateResponse>? _validateTask;

    public NexusClient(
        HttpClient http,
        INexusApiKeyProvider keyProvider,
        ILogger<NexusClient> logger)
    {
        _http = http;
        _keyProvider = keyProvider;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  Validate
    // ------------------------------------------------------------------

    /// <summary>
    /// Проверяет, является ли текущий API-ключ Premium-аккаунтом.
    ///
    /// Результат кешируется на время жизни клиента: первый успешный
    /// запрос к /validate.json запоминается, все последующие вызовы
    /// возвращают его же. Параллельные вызовы ждут одну Task — один
    /// HTTP-запрос на весь pipeline.
    ///
    /// Если предыдущий запрос был отменён или упал — следующий вызов
    /// сделает новый. Это важно для retry: ArchiveDownloadHelper
    /// повторит DownloadAsync, и мы не должны «отравить» его
    /// faulted-результатом.
    /// </summary>
    public async Task<bool> IsPremiumAsync(CancellationToken ct)
    {
        var response = await GetValidateInfoAsync(ct).ConfigureAwait(false);
        return response.IsPremium;
    }

    /// <summary>
    /// Возвращает полный ответ /users/validate.json (кешированный).
    /// Содержит IsPremium, Name, UserId. Нужен NexusDownloader-у,
    /// чтобы сверить user_id из nxm:// при Free-скачивании.
    ///
    /// Кешируется так же, как IsPremiumAsync — один запрос на весь
    /// pipeline. Faulted/canceled результат не кешируется.
    /// </summary>
    public async Task<NexusValidateResponse> GetValidateInfoAsync(CancellationToken ct)
    {
        Task<NexusValidateResponse> task;

        lock (_validateLock)
        {
            if (_validateTask is null
                || _validateTask.IsFaulted
                || _validateTask.IsCanceled)
            {
                _validateTask = FetchValidateAsync(ct);
            }

            task = _validateTask;
        }

        return await task.ConfigureAwait(false);
    }

    private async Task<NexusValidateResponse> FetchValidateAsync(CancellationToken ct)
    {
        var validate = await SendAsync<NexusValidateResponse>(
            "/users/validate.json",
            notFoundMessage: "Nexus rejected the API key: not a valid account.",
            ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Nexus validate: name={Name}, premium={IsPremium}, userId={UserId}",
            validate.Name ?? "<unknown>",
            validate.IsPremium,
            validate.UserId);

        return validate;
    }

    // ------------------------------------------------------------------
    //  Download links
    // ------------------------------------------------------------------

    /// <summary>
    /// Возвращает список CDN-ссылок для скачивания файла.
    ///
    /// Premium: nxmKey == null, nxmExpires == null.
    ///   URL: /games/{game}/mods/{modId}/files/{fileId}/download_link.json
    ///
    /// Free: nxmKey != null, nxmExpires != null (оба обязательны).
    ///   URL: /games/{game}/mods/{modId}/files/{fileId}/download_link.json
    ///        ?key={key}&amp;expires={expires}
    ///
    /// В обоих случаях ставится apikey-заголовок. Без него Nexus
    /// вернёт 401.
    /// </summary>
    /// <param name="game">Nexus game domain.</param>
    /// <param name="modId">ID мода.</param>
    /// <param name="fileId">ID файла.</param>
    /// <param name="nxmKey">
    /// Временный download-ключ из nxm://. Только для Free-сценария.
    /// </param>
    /// <param name="nxmExpires">
    /// Unix timestamp (секунды) истечения nxmKey. Только для Free-сценария.
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<IReadOnlyList<NexusDownloadLink>> GetDownloadLinksAsync(
        string game,
        int modId,
        int fileId,
        string? nxmKey = null,
        long? nxmExpires = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(game))
            throw new ArgumentException(
                "Game domain must be non-empty.", nameof(game));
        if (modId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(modId), "modId must be positive.");
        if (fileId <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(fileId), "fileId must be positive.");

        // key/expires — либо оба, либо ни одного.
        bool hasKey = !string.IsNullOrEmpty(nxmKey);
        bool hasExpires = nxmExpires.HasValue;

        if (hasKey != hasExpires)
        {
            throw new ArgumentException(
                "nxmKey and nxmExpires must be both set or both null.",
                hasKey ? nameof(nxmExpires) : nameof(nxmKey));
        }

        var path = $"/games/{Uri.EscapeDataString(game)}/mods/{modId}" +
                   $"/files/{fileId}/download_link.json";

        if (hasKey && hasExpires)
        {
            path += $"?key={Uri.EscapeDataString(nxmKey!)}&expires={nxmExpires!.Value}";
        }

        var links = await SendAsync<NexusDownloadLink[]>(
            path,
            notFoundMessage: $"Nexus mod/file not found: {game}/{modId}/{fileId}.",
            ct).ConfigureAwait(false);

        // Nexus иногда возвращает null-элементы или элементы без URI —
        // фильтруем сразу, чтобы NexusDownloader не проверял.
        var usable = links
            .Where(l => l?.Uri is not null)
            .ToArray();

        if (usable.Length != links.Length)
        {
            _logger.LogWarning(
                "Nexus returned {Total} download links for {Game}/{Mod}/{File}, " +
                "{Usable} of them usable",
                links.Length, game, modId, fileId, usable.Length);
        }

        return usable;
    }

    // ------------------------------------------------------------------
    //  HTTP
    // ------------------------------------------------------------------

    private async Task<T> SendAsync<T>(
        string relativePath,
        string notFoundMessage,
        CancellationToken ct)
    {
        var key = _keyProvider.TryGetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new NexusAuthenticationException(
                "Nexus API key is not set. Authenticate with Nexus to continue.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, BaseUrl + relativePath);

        request.Headers.Add("apikey", key);
        request.Headers.Add("Application-Name", ApplicationName);
        request.Headers.Add("Application-Version", ApplicationVersion);
        request.Headers.Add("User-Agent", UserAgent);
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                "application/json"));

        _logger.LogDebug("Nexus API GET {Path}", relativePath);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content
                .ReadFromJsonAsync<T>(cancellationToken: ct)
                .ConfigureAwait(false);

            if (result is null)
            {
                throw new InvalidOperationException(
                    $"Nexus API returned an empty body for {relativePath}.");
            }

            return result;
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new NexusAuthenticationException(
                    "Nexus rejected the API key.");

            case HttpStatusCode.Forbidden:
                throw new InvalidOperationException(
                    "Nexus Premium is required for automatic downloads. " +
                    "Log in with a Premium account or download manually.");

            case HttpStatusCode.NotFound:
                throw new InvalidOperationException(notFoundMessage);

            case HttpStatusCode.TooManyRequests:
                throw new InvalidOperationException(
                    "Nexus API rate limit exceeded. Wait a minute and retry.");

            default:
                throw new HttpRequestException(
                    $"Nexus API returned {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}) for {relativePath}.");
        }
    }
}
