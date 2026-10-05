// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Matching;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Собирает ModlistManifest из результатов предыдущих шагов.
///
/// Вход: PackConfig, InstanceSnapshot, ArchiveIndex, ModScanResult, MatchResult,
///       EntryScanResult (extensions), EntryScanResult (extras),
///       MatchEntriesResult (extensions), MatchEntriesResult (extras).
///
/// Выход: ModlistManifest.
///
/// mo2.extensions и stockGame.extras заполняются из Match*Results.
/// Entry с пустым списком директив ПРОПУСКАЮТСЯ: unmatched extensions/extras
/// выгружены в __ModsyncManager_Output, а в манифесте пустая entry бессмысленна
/// (installer её всё равно скипнет). Это отличается от модов, где entry
/// в манифесте соответствует строке modlist.txt — внешнему источнику истины.
///
/// MO2-архив:
///  - Строится через Mo2ArchiveBuilder.Build (общий helper с PackPipeline).
///  - MO2-архив НЕ попадает в manifest.Archives[] — он только
///    в manifest.Mo2.Archive.
///
/// Моды [NoDelete] исключаются из манифеста. Их order учитывается
/// (индекс в modlist.txt), поэтому у соседних модов order может иметь дырки.
///
/// Сепараторы (#...) остаются в манифесте с пустыми директивами.
/// Installer сам решит их пропустить при раскладке.
/// </summary>
public sealed class BuildManifestStep : IStep<BuildManifestStep.Input, ModlistManifest>
{
    private const string SchemaVersion = "1.0.0";
    private const string CreatedBy = "modsyncmanager-pack/0.1.0";

    private readonly ILogger<BuildManifestStep> _logger;

    public BuildManifestStep(ILogger<BuildManifestStep> logger)
    {
        _logger = logger;
    }

    public Task<ModlistManifest> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Building manifest for '{Name}' v{Version}",
            input.Config.Meta.Name, input.Config.Meta.Version);

        var mods = BuildMods(input);
        var plugins = BuildPlugins(input);
        var loadorder = input.Snapshot.Loadorder.Plugins.ToList();

        var mo2Archive = Mo2ArchiveBuilder.Build(
            input.Config, input.ArchiveIndex,
            input.Snapshot.DownloadsPath, _logger);

        var extensions = BuildExtensions(input);
        var extras = BuildExtras(input);

        // MO2-архив — отдельная сущность. В manifest.Archives[] его быть не должно,
        // даже если IndexArchivesStep положил его в Resolved (например, если
        // пользователь добавил Mod.Organizer-2.5.2.7z в archiveSources[]).
        var nonMo2Archives = input.ArchiveIndex.Resolved
            .Where(a => !string.Equals(
                a.Name, input.Config.Mo2.Archive, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var manifest = new ModlistManifest
        {
            SchemaVersion = SchemaVersion,
            ManifestVersion = input.Config.Meta.Version,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = CreatedBy,

            Meta = new ManifestMeta
            {
                Name = input.Config.Meta.Name,
                Version = input.Config.Meta.Version,
                Author = input.Config.Meta.Author,
                Game = input.Config.Meta.Game,
                GameVersion = input.Config.Meta.GameVersion,
            },

            Execution = new ExecutionPolicy(),

            Mo2 = new Mo2Section
            {
                Version = input.Config.Mo2.Version,
                Profile = input.Config.Mo2.Profile,
                Archive = mo2Archive,
                Extensions = extensions,
            },

            StockGame = new StockGameSection
            {
                Extras = extras,
            },

            Archives = nonMo2Archives,
            Mods = mods,
            Plugins = plugins,
            Loadorder = loadorder,
        };

        int enabledMods = mods.Count(m => m.Enabled);
        int disabledMods = mods.Count - enabledMods;
        int withMeta = mods.Count(m => m.Meta is not null);

        _logger.LogInformation(
            "Manifest built: {Mods} mods ({Enabled} enabled, {Disabled} disabled, {WithMeta} with meta.ini), " +
            "{Plugins} plugins, {Loadorder} loadorder entries, {Archives} archives, " +
            "{Extensions} extensions, {Extras} extras",
            mods.Count, enabledMods, disabledMods, withMeta,
            plugins.Count, loadorder.Count, nonMo2Archives.Count,
            extensions.Count, extras.Count);

        return Task.FromResult(manifest);
    }

    // ------------------------------------------------------------------
    //  Mods
    // ------------------------------------------------------------------

    private static IReadOnlyList<ModEntry> BuildMods(Input input)
    {
        var entries = input.Snapshot.Modlist.Entries;
        var result = new List<ModEntry>(entries.Count);

        // order — индекс в исходном modlist.txt. [NoDelete]-моды занимают
        // позиции, но в манифест не попадают (дырки в нумерации допустимы).
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.Name.Contains("[NoDelete]", StringComparison.OrdinalIgnoreCase))
                continue;

            var directives = input.Match.ModDirectives.TryGetValue(entry.Name, out var d)
                ? d
                : Array.Empty<Modsync.Core.Models.Manifest.Directives.Directive>();

            var meta = input.Match.ModMetas.TryGetValue(entry.Name, out var m)
                ? m
                : null;

            result.Add(new ModEntry
            {
                Name = entry.Name,
                Enabled = entry.Enabled,
                Order = i,
                Meta = meta,
                Directives = directives,
            });
        }

        return result;
    }

    // ------------------------------------------------------------------
    //  Plugins
    // ------------------------------------------------------------------

    private static IReadOnlyList<PluginEntry> BuildPlugins(Input input)
    {
        var entries = input.Snapshot.Plugins.Entries;
        var result = new List<PluginEntry>(entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            result.Add(new PluginEntry
            {
                Name = entry.Name,
                Enabled = entry.Enabled,
                Order = i,
            });
        }

        return result;
    }

    // ------------------------------------------------------------------
    //  Extensions / Extras
    // ------------------------------------------------------------------

    /// <summary>
    /// Строит ExtensionEntry[] из MatchEntriesResult.
    ///
    /// Entry с пустым списком директив пропускаются: файл не матчится,
    /// unmatched уже в __ModsyncManager_Output, в манифесте entry без директив
    /// бессмысленна.
    /// </summary>
    private static IReadOnlyList<ExtensionEntry> BuildExtensions(Input input)
    {
        var result = new List<ExtensionEntry>(
            input.ExtensionsMatch.Directives.Count);

        foreach (var (entryName, directives) in input.ExtensionsMatch.Directives)
        {
            if (directives.Count == 0)
                continue;

            result.Add(new ExtensionEntry
            {
                Name = entryName,
                Directives = directives,
            });
        }

        return result;
    }

    /// <summary>
    /// Симметричен BuildExtensions для extras.
    /// </summary>
    private static IReadOnlyList<ExtensionEntry> BuildExtras(Input input)
    {
        var result = new List<ExtensionEntry>(
            input.ExtrasMatch.Directives.Count);

        foreach (var (entryName, directives) in input.ExtrasMatch.Directives)
        {
            if (directives.Count == 0)
                continue;

            result.Add(new ExtensionEntry
            {
                Name = entryName,
                Directives = directives,
            });
        }

        return result;
    }

    // ------------------------------------------------------------------
    //  Input
    // ------------------------------------------------------------------

    /// <summary>
    /// record — чтобы тесты могли использовать `with` для переопределения
    /// extensions/extras-полей поверх MakeInput(). Остальные поля — required.
    /// </summary>
    public sealed record Input
    {
        public required PackConfig Config { get; init; }
        public required InstanceSnapshot Snapshot { get; init; }
        public required ArchiveIndex ArchiveIndex { get; init; }
        public required ModScanResult ModScan { get; init; }
        public required MatchResult Match { get; init; }

        public required EntryScanResult ExtensionsScan { get; init; }
        public required EntryScanResult ExtrasScan { get; init; }
        public required MatchEntriesResult ExtensionsMatch { get; init; }
        public required MatchEntriesResult ExtrasMatch { get; init; }
    }
}
