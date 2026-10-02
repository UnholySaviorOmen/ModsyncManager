// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Pack;

/// <summary>
/// Плоская сводка результата packer-а. Только примитивы.
///
/// Единственный источник данных для отображения результата
/// (CLI-таблица, GUI-ViewModel). Не содержит ссылок на PackResult —
/// чтобы GUI мог получить Summary и не тащить за собой весь pipeline.
///
/// Заполняется PackSummaryBuilder.
/// </summary>
public sealed record PackSummary
{
    // --- Метаданные сборки ---
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Game { get; init; }

    // --- Инстанс ---
    public required string InstancePath { get; init; }

    // --- Профиль (числа, как в таблице PackCommand) ---
    public required int ModsTotal { get; init; }
    public required int PluginsTotal { get; init; }
    public required int LoadorderTotal { get; init; }

    // --- Архивы ---
    public required int ArchivesResolved { get; init; }
    public required int ArchivesUnresolved { get; init; }

    // --- Сканирование модов ---
    public required int ModsScanned { get; init; }
    public required int FilesScanned { get; init; }

    // --- Директивы ---
    public required int DirectivesTotal { get; init; }
    public required int DirectivesFromArchive { get; init; }

    // --- Unmatched / meta ---
    public required int UnmatchedFiles { get; init; }
    public required int MetaIniCount { get; init; }

    // --- Манифест ---
    public required int ManifestMods { get; init; }
    public required int ManifestArchives { get; init; }
    public required int ManifestExtensions { get; init; }
    public required int ManifestExtras { get; init; }

    // --- Пути для вывода ---
    /// <summary>
    /// Заполнено только если UnmatchedFiles > 0.
    /// Иначе null.
    /// </summary>
    public required string? UnmatchedWrittenTo { get; init; }

    public required string ManifestPath { get; init; }
}
