// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;

namespace Modsync.Install.Verify;

/// <summary>
/// Разделяемое состояние проверок: пути инстанса, манифест,
/// индекс хешей из downloads/.
///
/// Строится один раз в VerifyPipeline.ExecuteAsync, потом передаётся
/// в каждую проверку. Не изменяется после конструктора — read-only.
/// </summary>
public sealed class VerifyContext
{
    public required string TargetPath { get; init; }
    public required ModlistManifest Manifest { get; init; }

    public required string Mo2Path { get; init; }
    public required string DownloadsPath { get; init; }
    public required string ModsPath { get; init; }
    public required string ProfilesPath { get; init; }
    public required string StockGamePath { get; init; }

    /// <summary>
    /// Хеш → путь в downloads/ для всех файлов в downloads/.
    /// Строится один раз до всех проверок.
    /// </summary>
    public required IReadOnlyDictionary<XxHash64Value, string> DownloadsByHash { get; init; }

    /// <summary>
    /// Профиль из манифеста. Если пусто — проверки профиля скипаются.
    /// </summary>
    public string Profile => Manifest.Mo2.Profile;
}
