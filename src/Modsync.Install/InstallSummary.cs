// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Install;

/// <summary>
/// Плоская сводка результата installer-а. Только примитивы.
///
/// Единственный источник данных для отображения результата
/// (CLI-таблица, GUI-ViewModel). Не содержит ссылок на
/// InstallPipeline.Output — чтобы GUI не тащил за собой pipeline.
///
/// Заполняется InstallSummaryBuilder.
/// </summary>
public sealed record InstallSummary
{
    // --- Метаданные ---
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Game { get; init; }

    // --- Инстанс ---
    public required string InstancePath { get; init; }
    public required string ManifestPathInInstance { get; init; }

    // --- Архивы ---
    public required int ArchivesAlreadyPresent { get; init; }
    public required int ArchivesDownloaded { get; init; }
    public required int ArchivesSkipped { get; init; }

    // --- Моды ---
    public required int ModsCreated { get; init; }
    public required int ModsRecreated { get; init; }
    public required int ModsSkipped { get; init; }
    public required int ModsDeleted { get; init; }

    // --- meta.ini ---
    public required int MetaIniWritten { get; init; }
    public required int MetaIniDeleted { get; init; }

    // --- MO2 extensions ---
    public required int ExtensionsWritten { get; init; }
    public required int ExtensionsSkipped { get; init; }

    // --- Stock Game extras ---
    public required int ExtrasWritten { get; init; }
    public required int ExtrasSkipped { get; init; }

    // --- Профиль ---
    public required int ProfileMods { get; init; }
    public required int ProfilePlugins { get; init; }
    public required int ProfileLoadorder { get; init; }
}
