// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace Modsync.Platform.Nexus.Models;

/// <summary>
/// Ответ /v1/users/validate.json.
///
/// Берём только то, что нужно:
///   - is_premium (для проверки перед скачиванием),
///   - name (для логов),
///   - user_id (для сверки с user_id из nxm:// при Free-скачивании).
///
/// Публичный тип: NexusClient.GetValidateInfoAsync возвращает его,
/// чтобы клиенты (GUI) могли получить статус без повторного запроса.
/// </summary>
public sealed class NexusValidateResponse
{
    [JsonPropertyName("is_premium")]
    public bool IsPremium { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("user_id")]
    public int UserId { get; init; }
}
