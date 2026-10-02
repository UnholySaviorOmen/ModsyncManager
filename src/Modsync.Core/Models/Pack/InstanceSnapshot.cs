// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Mo2;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Снимок инстанса MO2. Результат ReadInstanceStep.
/// Содержит пути и прочитанные файлы профиля. Не содержит хешей и директив.
/// </summary>
public sealed record InstanceSnapshot
{
    /// <summary>Корень инстанса (папка, содержащая MO2/ и Stock Game/).</summary>
    public required string InstancePath { get; init; }

    /// <summary>Корень MO2 (папка с ModOrganizer.exe).</summary>
    public required string Mo2Path { get; init; }

    /// <summary>MO2/downloads/ — папка с архивами.</summary>
    public required string DownloadsPath { get; init; }

    /// <summary>MO2/mods/ — папка с установленными модами.</summary>
    public required string ModsPath { get; init; }

    /// <summary>MO2/profiles/ — папка с профилями.</summary>
    public required string ProfilesPath { get; init; }

    /// <summary>Stock Game/ — папка с extras.</summary>
    public required string StockGamePath { get; init; }

    /// <summary>
    /// __ModsyncManager_Output/ в корне инстанса.
    /// MatchStep выгружает сюда unmatched-файлы с сохранением структуры mods/&lt;ModName&gt;/&lt;path&gt;.
    /// Не пересекается с mods/ и MO2/ — ScanModsStep её не видит.
    /// </summary>
    public required string ModsyncOutputPath { get; init; }

    public required ModlistFile Modlist { get; init; }
    public required PluginsFile Plugins { get; init; }
    public required LoadorderFile Loadorder { get; init; }
}
