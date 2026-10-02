// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Modsync.Platform.Nexus.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus;

public enum NexusKeyStatus
{
    /// <summary>Ключ валиден.</summary>
    Valid,

    /// <summary>Ключ невалиден (401/403), либо аккаунт не существует.</summary>
    Invalid,

    /// <summary>Не удалось проверить (сеть, DNS, timeout, 5xx, 429).</summary>
    NetworkError,
}

public sealed record NexusValidationResult(
    NexusKeyStatus Status,
    string? UserName,
    bool IsPremium,
    string? ErrorMessage);

public interface INexusCredentialValidator
{
    /// <summary>
    /// Проверить API-ключ через /v1/users/validate.json.
    /// Никогда не бросает. Все ошибки — через NexusValidationResult.
    /// </summary>
    Task<NexusValidationResult> ValidateAsync(
        string apiKey, CancellationToken ct);
}

/// <summary>
/// Проверяет API-ключ через /v1/users/validate.json.
///
/// Отдельный сервис, а не метод NexusClient: валидация нужна ДО
/// сохранения ключа, когда NexusClient ещё не построен (он требует
/// уже сохранённый ключ из провайдера).
///
/// Http-клиент берётся из IHttpClientFactory по имени "nexus-api"
/// (таймаут 2 минуты — уже настроен в DI).
///
/// Никогда не бросает. Все ошибки — через NexusValidationResult.
/// Исключение: OperationCanceledException, если отменён ct.
/// </summary>
public sealed class NexusCredentialValidator : INexusCredentialValidator
{
    private const string BaseUrl = "https://api.nexusmods.com/v1";
    private const string ApplicationName = "ModsyncManager";
    private const string ApplicationVersion = "0.1.0";
    private const string UserAgent = "ModsyncManager/0.1.0";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NexusCredentialValidator> _logger;

    public NexusCredentialValidator(
        IHttpClientFactory httpClientFactory,
        ILogger<NexusCredentialValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<NexusValidationResult> ValidateAsync(
        string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new NexusValidationResult(
                NexusKeyStatus.Invalid,
                null,
                false,
                "API key is empty.");
        }

        var http = _httpClientFactory.CreateClient("nexus-api");

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{BaseUrl}/users/validate.json");

            request.Headers.Add("apikey", apiKey.Trim());
            request.Headers.Add("Application-Name", ApplicationName);
            request.Headers.Add("Application-Version", ApplicationVersion);
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content
                    .ReadFromJsonAsync<NexusValidateResponse>(ct);

                if (body is null)
                {
                    return new NexusValidationResult(
                        NexusKeyStatus.Invalid,
                        null,
                        false,
                        "Nexus returned an empty body.");
                }

                _logger.LogDebug(
                    "Nexus key validated: name={Name}, premium={IsPremium}",
                    body.Name ?? "<unknown>", body.IsPremium);

                return new NexusValidationResult(
                    NexusKeyStatus.Valid,
                    body.Name,
                    body.IsPremium,
                    null);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    new NexusValidationResult(
                        NexusKeyStatus.Invalid,
                        null,
                        false,
                        "Nexus rejected the API key."),

                HttpStatusCode.Forbidden =>
                    new NexusValidationResult(
                        NexusKeyStatus.Invalid,
                        null,
                        false,
                        "Nexus refused the API key."),

                HttpStatusCode.TooManyRequests =>
                    new NexusValidationResult(
                        NexusKeyStatus.NetworkError,
                        null,
                        false,
                        "Nexus rate limit exceeded. Try again in a minute."),

                _ => new NexusValidationResult(
                        NexusKeyStatus.NetworkError,
                        null,
                        false,
                        $"Nexus returned {(int)response.StatusCode} " +
                        $"({response.ReasonPhrase})."),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // HttpClient выбросил TaskCanceledException по таймауту
            // (не по отмене ct — это отфильтровано выше).
            _logger.LogWarning("Nexus validation request timed out");
            return new NexusValidationResult(
                NexusKeyStatus.NetworkError,
                null,
                false,
                "Nexus did not respond in time.");
        }
        catch (JsonException ex)
        {
            // 200 OK, но body — не то, что мы ожидаем.
            // Это не сетевая ошибка: сервер ответил, но ответ невалидный.
            _logger.LogWarning(ex,
                "Nexus returned malformed JSON from validate.json");
            return new NexusValidationResult(
                NexusKeyStatus.Invalid,
                null,
                false,
                "Nexus returned malformed JSON.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Nexus validation request failed");
            return new NexusValidationResult(
                NexusKeyStatus.NetworkError,
                null,
                false,
                $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error validating Nexus key");
            return new NexusValidationResult(
                NexusKeyStatus.NetworkError,
                null,
                false,
                $"Unexpected error: {ex.Message}");
        }
    }
}
