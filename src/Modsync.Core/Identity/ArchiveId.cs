// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace Modsync.Core.Identity;

/// <summary>
/// Генератор канонических id архивов.
/// Формат:
///   nexus_{game_domain}_{modId}_{fileId}   — для Nexus-модов (есть .meta)
///   local_{slug}                            — для архивов без .meta
/// </summary>
public static class ArchiveId
{
    public const string NexusPrefix = "nexus_";
    public const string LocalPrefix = "local_";

    /// <summary>
    /// Канонический id для Nexus-архива.
    /// </summary>
    public static string FromNexus(string gameDomain, int modId, int fileId)
    {
        if (string.IsNullOrWhiteSpace(gameDomain))
            throw new ArgumentException("Game domain must be non-empty.", nameof(gameDomain));
        if (modId <= 0)
            throw new ArgumentOutOfRangeException(nameof(modId), "modId must be positive.");
        if (fileId <= 0)
            throw new ArgumentOutOfRangeException(nameof(fileId), "fileId must be positive.");

        var game = gameDomain.Trim().ToLowerInvariant();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{NexusPrefix}{game}_{modId}_{fileId}");
    }

    /// <summary>
    /// Id для архива без .meta. Использует slug от имени файла (без расширения).
    /// </summary>
    public static string FromLocal(string fileName)
    {
        var slug = Slug.FromFileName(fileName);
        return LocalPrefix + slug;
    }
}
