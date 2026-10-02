// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus;

/// <summary>
/// Разобранный nxm://-URL.
///
/// Формат (зафиксирован Nexus):
///   nxm://{game}/mods/{modId}/files/{fileId}
///       ?key={key}&amp;expires={expires}&amp;user_id={userId}
///
/// Все query-параметры опциональны. При их отсутствии Key=null,
/// Expires=null, UserId=null. Это валидное состояние: Nexus генерирует
/// URL без key/expires для Premium-аккаунтов.
///
/// Если Key задан — Expires и UserId тоже должны быть заданы. Это
/// проверяет NexusUrlParser (не эта модель).
/// </summary>
public sealed record NexusNxmUrl
{
    /// <summary>Nexus game domain (например, "skyrimspecialedition").</summary>
    public required string Game { get; init; }

    /// <summary>ID мода на Nexus (положительный).</summary>
    public required int ModId { get; init; }

    /// <summary>ID файла на Nexus (положительный).</summary>
    public required int FileId { get; init; }

    /// <summary>
    /// Временный download-ключ из nxm://. Null — если Nexus не сгенерировал
    /// (Premium-сценарий).
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Unix timestamp (секунды) истечения Key. Null — если Key отсутствует.
    /// </summary>
    public long? Expires { get; init; }

    /// <summary>
    /// ID пользователя Nexus, для которого сгенерирован Key. Null — если
    /// Key отсутствует. Сравнивается с user_id из /users/validate.json
    /// перед использованием download_link?key=...&amp;expires=...
    /// </summary>
    public int? UserId { get; init; }

    /// <summary>
    /// true, если Key/Expires/UserId заданы — то есть URL пригоден для
    /// Free-скачивания через download_link.
    /// </summary>
    public bool HasFreeCredentials =>
        Key is not null && Expires is not null && UserId is not null;
}
