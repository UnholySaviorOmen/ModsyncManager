// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace Modsync.Platform.Nexus.Models;

/// <summary>
/// Один CDN-узел Nexus из ответа /download_link.json.
///
/// Nexus возвращает массив таких объектов (обычно несколько CDN-нод).
/// NexusDownloader перебирает их по очереди: если одна упала —
/// пробует следующую.
///
/// Имена полей — точно как у Nexus: "name", "short_name", "URI".
/// </summary>
public sealed class NexusDownloadLink
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; init; }

    [JsonPropertyName("URI")]
    public Uri? Uri { get; init; }
}
