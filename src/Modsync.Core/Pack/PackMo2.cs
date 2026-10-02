// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Core.Models.Pack;

public sealed record PackMo2
{
    public required string Version { get; init; }

    /// <summary>
    /// Имя профиля MO2 в profiles/ (например, "NordicUI").
    /// Папка profiles/{Profile}/ содержит modlist.txt, plugins.txt, loadorder.txt.
    /// </summary>
    public required string Profile { get; init; }

    /// <summary>Имя архива MO2 в downloads/ (например, "Mod.Organizer-2.5.2.7z").</summary>
    public required string Archive { get; init; }

    /// <summary>Источник для скачивания архива MO2.</summary>
    public required ArchiveSourceRef Source { get; init; }

    /// <summary>
    /// Пути к файлам/папкам extensions относительно корня MO2/.
    /// Например: "plugins/fomod_plus_installer.dll", "tools/BethINI/".
    /// </summary>
    public required IReadOnlyList<string> Extensions { get; init; }
}
