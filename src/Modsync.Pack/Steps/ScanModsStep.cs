// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Сканирует папку mods/ — считает хеши всех файлов модов из modlist.txt.
///
/// Сканируются ВСЕ моды (и включённые, и отключённые), кроме модов
/// с [NoDelete] в имени. Отключённые моды могут содержать опционально
/// подключаемый контент — их файлы тоже нужны в манифесте.
///
/// Хеши берутся через IHashCache (L1 in-memory + L2 SQLite).
/// Пути к файлам модов стабильны между прогонами → L2 работает.
///
/// Отсутствие папки для включённого мода — ОШИБКА.
/// Отсутствие папки для отключённого мода — предупреждение.
/// Отсутствие папки для сепаратора (#...) — debug.
/// </summary>
public sealed class ScanModsStep : IStep<ScanModsStep.Input, ModScanResult>
{
    private const string NoDeleteMarker = "[NoDelete]";
    private const char SeparatorPrefix = '#';

    private readonly IHashCache _hashCache;
    private readonly ILogger<ScanModsStep> _logger;

    public ScanModsStep(IHashCache hashCache, ILogger<ScanModsStep> logger)
    {
        _hashCache = hashCache;
        _logger = logger;
    }

    public Task<ModScanResult> ExecuteAsync(Input input, CancellationToken ct)
    {
        var entries = input.Snapshot.Modlist.Entries;

        var toScan = entries
            .Where(e => !e.Name.Contains(NoDeleteMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var enabledCount = toScan.Count(e => e.Enabled);
        var disabledCount = toScan.Count(e => !e.Enabled);

        _logger.LogInformation(
            "ScanModsStep: scanning {Total} mods ({Enabled} enabled, {Disabled} disabled) out of {All}",
            toScan.Count, enabledCount, disabledCount, entries.Count);

        var existingEntries = new List<Modsync.Core.Models.Mo2.ModlistEntry>(toScan.Count);
        foreach (var entry in toScan)
        {
            var path = Path.Combine(input.Snapshot.ModsPath, entry.Name);
            if (Directory.Exists(path))
            {
                existingEntries.Add(entry);
            }
            else if (entry.Enabled)
            {
                throw new DirectoryNotFoundException(
                    $"Mod directory not found: {path} " +
                    $"(mod '{entry.Name}' is enabled in modlist.txt)");
            }
            else if (IsSeparator(entry.Name))
            {
                _logger.LogDebug(
                    "Separator '{Name}' has no folder (expected) — skipping",
                    entry.Name);
            }
            else
            {
                _logger.LogWarning(
                    "Mod directory not found for disabled mod (skipping): {Mod}",
                    entry.Name);
            }
        }

        var results = new ConcurrentDictionary<string, IReadOnlyList<ScannedFile>>(
            StringComparer.Ordinal);

        Parallel.ForEach(
            existingEntries,
            input.ParallelOptions,
            () => 0,
            (entry, _, _) =>
            {
                ct.ThrowIfCancellationRequested();

                var modPath = Path.Combine(input.Snapshot.ModsPath, entry.Name);
                var files = ScanOneMod(modPath, ct);

                results[entry.Name] = files;

                _logger.LogDebug(
                    "Scanned mod '{Mod}': {Count} files (enabled={Enabled})",
                    entry.Name, files.Count, entry.Enabled);

                return 0;
            },
            _ => { });

        var ordered = results
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

        var totalFiles = ordered.Values.Sum(v => v.Count);
        _logger.LogInformation(
            "ScanModsStep: {Mods} mods scanned, {Files} files total",
            ordered.Count, totalFiles);

        return Task.FromResult(new ModScanResult
        {
            Mods = ordered,
        });
    }

    private static bool IsSeparator(string name)
        => name.Length > 0 && name[0] == SeparatorPrefix;

    private IReadOnlyList<ScannedFile> ScanOneMod(string modPath, CancellationToken ct)
    {
        var result = new List<ScannedFile>();

        var files = Directory.EnumerateFiles(
            modPath,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
            });

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(modPath, file).Replace('\\', '/');
            var hash = _hashCache.GetOrCompute(file);
            var size = new FileInfo(file).Length;

            result.Add(new ScannedFile
            {
                RelativePath = relative,
                Hash = hash,
                Size = size,
            });
        }

        result.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        return result;
    }

    public sealed class Input
    {
        public required InstanceSnapshot Snapshot { get; init; }
        public required ParallelOptions ParallelOptions { get; init; }
    }
}
