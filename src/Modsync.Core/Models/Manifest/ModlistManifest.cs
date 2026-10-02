// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest.Directives;

namespace Modsync.Core.Models.Manifest;

public sealed class ModlistManifest
{
    public required string SchemaVersion { get; init; }
    public required string ManifestVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string CreatedBy { get; init; }

    public required ManifestMeta Meta { get; init; }
    public required ExecutionPolicy Execution { get; init; }
    public required Mo2Section Mo2 { get; init; }
    public required StockGameSection StockGame { get; init; }
    public required IReadOnlyList<ArchiveEntry> Archives { get; init; }
    public required IReadOnlyList<ModEntry> Mods { get; init; }
    public required IReadOnlyList<PluginEntry> Plugins { get; init; }
    public required IReadOnlyList<string> Loadorder { get; init; }
}

public sealed class ManifestMeta
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public required string Game { get; init; }
    public required string GameVersion { get; init; }
}

public sealed class ExecutionPolicy
{
    public string Directives { get; init; } = "sequential";
    public string OnConflict { get; init; } = "lastWins";
}

public sealed class Mo2Section
{
    public required string Version { get; init; }

    /// <summary>
    /// Имя профиля MO2 в profiles/. Например, "Default" или "NordicUI".
    /// Используется installer-ом при генерации файлов профиля.
    /// </summary>
    public required string Profile { get; init; }

    public required ArchiveEntry Archive { get; init; }
    public required IReadOnlyList<ExtensionEntry> Extensions { get; init; }
}

public sealed class ExtensionEntry
{
    public required string Name { get; init; }
    public required IReadOnlyList<Directive> Directives { get; init; }
}

public sealed class StockGameSection
{
    public required IReadOnlyList<ExtensionEntry> Extras { get; init; }
}

public sealed class ModEntry
{
    public required string Name { get; init; }
    public required bool Enabled { get; init; }
    public required int Order { get; init; }

    /// <summary>
    /// Structured meta.ini из mods/&lt;Name&gt;/meta.ini.
    /// Null, если meta.ini нет или папки мода нет на диске.
    /// Не null, даже если секция [General] пустая (IsEmpty == true).
    /// </summary>
    public ModMeta? Meta { get; init; }

    public required IReadOnlyList<Directive> Directives { get; init; }
}

public sealed class PluginEntry
{
    public required string Name { get; init; }
    public required bool Enabled { get; init; }
    public required int Order { get; init; }
}
