// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Валидирует ModlistManifest и пропускает его дальше.
/// In == out, чтобы встраиваться в pipeline без склейки.
///
/// Бросает InvalidOperationException со списком всех ошибок, если что-то не так.
///
/// Что проверяем:
///  1. Уникальность archives[].id.
///  2. Уникальность mods[].name.
///  3. Уникальность plugins[].name.
///  4. Все FromArchiveDirective.Archive ссылаются на archives[].id
///     (или на mo2.archive.id).
///  5. mo2.archive: id, name, sources не пустые.
///  6. loadorder: без дубликатов.
///  7. mo2.extensions[] и stockGame.extras[]: entry.Name — префикс
///     для Destination всех FromArchive-директив entry.
///  8. mo2.extensions[] и stockGame.extras[]: entries не пересекаются
///     (никакая пара не является префиксом друг друга).
/// </summary>
public sealed class ValidateManifestStep : IStep<ModlistManifest, ModlistManifest>
{
    private readonly ILogger<ValidateManifestStep> _logger;

    public ValidateManifestStep(ILogger<ValidateManifestStep> logger)
    {
        _logger = logger;
    }

    public Task<ModlistManifest> ExecuteAsync(ModlistManifest input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogInformation("Validating manifest '{Name}'", input.Meta.Name);

        var errors = new List<string>();

        var archiveIds = CollectArchiveIds(input, errors);
        ValidateModNames(input, errors);
        ValidatePluginNames(input, errors);
        ValidateDirectives(input, archiveIds, errors);
        ValidateMo2Archive(input, errors);
        ValidateLoadorder(input, errors);
        ValidateExtensionEntries(
            input.Mo2.Extensions,
            "mo2.extensions",
            errors);
        ValidateExtensionEntries(
            input.StockGame.Extras,
            "stockGame.extras",
            errors);
        ValidateNoOverlap(
            input.Mo2.Extensions,
            "mo2.extensions",
            errors);
        ValidateNoOverlap(
            input.StockGame.Extras,
            "stockGame.extras",
            errors);

        if (errors.Count > 0)
        {
            var message = "Manifest validation failed:" +
                          Environment.NewLine +
                          string.Join(Environment.NewLine,
                              errors.Select(e => "  - " + e));
            throw new InvalidOperationException(message);
        }

        int directiveCount = input.Mods.Sum(m => m.Directives.Count);
        int archiveCount = input.Archives.Count + 1;
        int extEntries = input.Mo2.Extensions.Count;
        int xstEntries = input.StockGame.Extras.Count;

        _logger.LogInformation(
            "Manifest validated: {Archives} archives, {Mods} mods, " +
            "{Directives} directives, {Ext} extensions, {Xst} extras checked",
            archiveCount, input.Mods.Count, directiveCount,
            extEntries, xstEntries);

        return Task.FromResult(input);
    }

    // ------------------------------------------------------------------
    //  Проверки
    // ------------------------------------------------------------------

    private static HashSet<string> CollectArchiveIds(
        ModlistManifest manifest, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var archive in manifest.Archives)
        {
            if (string.IsNullOrWhiteSpace(archive.Id))
            {
                errors.Add("archives[].id must be non-empty.");
                continue;
            }

            if (!seen.Add(archive.Id))
            {
                errors.Add($"Duplicate archive id: '{archive.Id}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Mo2.Archive.Id))
        {
            if (!seen.Add(manifest.Mo2.Archive.Id))
            {
                errors.Add(
                    $"Duplicate archive id: '{manifest.Mo2.Archive.Id}' " +
                    $"(used by mo2.archive and archives[]).");
            }
        }

        return seen;
    }

    private static void ValidateModNames(ModlistManifest manifest, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mod in manifest.Mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name))
            {
                errors.Add("mods[].name must be non-empty.");
                continue;
            }

            if (!seen.Add(mod.Name))
            {
                errors.Add($"Duplicate mod name: '{mod.Name}'.");
            }
        }
    }

    private static void ValidatePluginNames(ModlistManifest manifest, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var plugin in manifest.Plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.Name))
            {
                errors.Add("plugins[].name must be non-empty.");
                continue;
            }

            if (!seen.Add(plugin.Name))
            {
                errors.Add($"Duplicate plugin name: '{plugin.Name}'.");
            }
        }
    }

    private static void ValidateDirectives(
        ModlistManifest manifest,
        HashSet<string> archiveIds,
        List<string> errors)
    {
        foreach (var mod in manifest.Mods)
        {
            for (int i = 0; i < mod.Directives.Count; i++)
            {
                var directive = mod.Directives[i];

                if (directive is FromArchiveDirective fromArchive)
                {
                    if (string.IsNullOrWhiteSpace(fromArchive.Archive))
                    {
                        errors.Add(
                            $"mods['{mod.Name}'].directives[{i}]: " +
                            $"FromArchive.Archive must be non-empty.");
                    }
                    else if (!archiveIds.Contains(fromArchive.Archive))
                    {
                        errors.Add(
                            $"mods['{mod.Name}'].directives[{i}]: " +
                            $"FromArchive.Archive '{fromArchive.Archive}' " +
                            $"does not exist in archives[].");
                    }
                }
            }
        }
    }

    private static void ValidateMo2Archive(ModlistManifest manifest, List<string> errors)
    {
        var archive = manifest.Mo2.Archive;

        if (string.IsNullOrWhiteSpace(archive.Id))
            errors.Add("mo2.archive.id must be non-empty.");

        if (string.IsNullOrWhiteSpace(archive.Name))
            errors.Add("mo2.archive.name must be non-empty.");

        if (archive.Sources.Count == 0)
            errors.Add("mo2.archive.sources must contain at least one source.");
    }

    private static void ValidateLoadorder(ModlistManifest manifest, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < manifest.Loadorder.Count; i++)
        {
            var name = manifest.Loadorder[i];

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"loadorder[{i}] must be non-empty.");
                continue;
            }

            if (!seen.Add(name))
            {
                errors.Add($"Duplicate plugin in loadorder: '{name}'.");
            }
        }
    }

    /// <summary>
    /// Проверяет, что для каждого ExtensionEntry его Name — префикс
    /// для Destination всех FromArchive-директив.
    ///
    /// Условие: normalizedDest == normalizedName
    ///          OR normalizedDest.StartsWith(normalizedName + "/").
    /// </summary>
    private static void ValidateExtensionEntries(
        IReadOnlyList<ExtensionEntry> entries,
        string sectionName,
        List<string> errors)
    {
        for (int entryIdx = 0; entryIdx < entries.Count; entryIdx++)
        {
            var entry = entries[entryIdx];

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                errors.Add($"{sectionName}[{entryIdx}].name must be non-empty.");
                continue;
            }

            var normalizedName = NormalizePath(entry.Name);

            if (normalizedName.Length == 0)
            {
                errors.Add(
                    $"{sectionName}[{entryIdx}].name normalizes to empty " +
                    $"string: '{entry.Name}'.");
                continue;
            }

            var entryPrefix = normalizedName + "/";

            for (int dirIdx = 0; dirIdx < entry.Directives.Count; dirIdx++)
            {
                var directive = entry.Directives[dirIdx];

                if (directive is not FromArchiveDirective fromArchive)
                    continue;

                if (string.IsNullOrWhiteSpace(fromArchive.Destination))
                    continue;

                var normalizedDest = NormalizePath(fromArchive.Destination);

                var matches =
                    string.Equals(
                        normalizedDest, normalizedName,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    normalizedDest.StartsWith(
                        entryPrefix,
                        StringComparison.OrdinalIgnoreCase);

                if (!matches)
                {
                    errors.Add(
                        $"{sectionName}[{entryIdx}] ('{entry.Name}') " +
                        $"directives[{dirIdx}]: FromArchive.Destination " +
                        $"'{fromArchive.Destination}' is not under entry " +
                        $"path. Must equal '{entry.Name}' or start with " +
                        $"'{normalizedName}/'.");
                }
            }
        }
    }

    /// <summary>
    /// Проверяет, что entries не пересекаются: никакая пара не
    /// является префиксом друг друга.
    ///
    /// Условие пересечения для пары (a, b), где a и b — нормализованные
    /// имена:
    ///   - a == b (дубликат, регистронезависимо);
    ///   - a.StartsWith(b + "/");
    ///   - b.StartsWith(a + "/").
    ///
    /// Пустые имена пропускаются — их уже отловила ValidateExtensionEntries.
    /// </summary>
    private static void ValidateNoOverlap(
        IReadOnlyList<ExtensionEntry> entries,
        string sectionName,
        List<string> errors)
    {
        // Готовим нормализованные имена.
        var normalized = new (int Index, string Original, string Normalized)[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            var original = entries[i].Name;
            normalized[i] = (
                i,
                original,
                string.IsNullOrWhiteSpace(original)
                    ? ""
                    : NormalizePath(original));
        }

        for (int i = 0; i < normalized.Length; i++)
        {
            var a = normalized[i];
            if (a.Normalized.Length == 0) continue;

            for (int j = i + 1; j < normalized.Length; j++)
            {
                var b = normalized[j];
                if (b.Normalized.Length == 0) continue;

                if (IsOverlap(a.Normalized, b.Normalized))
                {
                    errors.Add(
                        $"{sectionName}: '{a.Original}' (index {a.Index}) " +
                        $"and '{b.Original}' (index {b.Index}) overlap. " +
                        $"Each entry must be independent — either remove " +
                        $"the parent entry or remove the child entry.");
                }
            }
        }
    }

    /// <summary>
    /// true, если a и b пересекаются: совпадают или один вложен в другой.
    /// Оба аргумента — нормализованные пути (непустые).
    /// </summary>
    private static bool IsOverlap(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        if (a.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Приводит путь к нормализованной форме: \ → /, обрезка
    /// ведущих/хвостовых слэшей.
    /// </summary>
    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        normalized = normalized.TrimStart('/').TrimEnd('/');
        return normalized;
    }
}
