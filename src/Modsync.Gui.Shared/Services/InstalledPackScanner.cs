// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest;
using Modsync.Gui.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Реализация IInstalledPackScanner поверх файловой системы.
///
/// Корень передаётся в конструктор (а не берётся из AppContext
/// внутри) — чтобы тесты могли подсунуть temp-папку.
/// В DI регистрируется фабрика с
/// AppContext.BaseDirectory + "Instances".
///
/// Симметрично ResolveTargetStep: installer без --target кладёт
/// инстансы именно сюда (решение №74).
/// </summary>
internal sealed class InstalledPackScanner : IInstalledPackScanner
{
    private const string ManifestFileName = "modlist.json";

    private readonly string _instancesRoot;
    private readonly ILogger<InstalledPackScanner> _logger;

    public InstalledPackScanner(
        string instancesRoot,
        ILogger<InstalledPackScanner> logger)
    {
        _instancesRoot = instancesRoot;
        _logger = logger;
    }

    public IReadOnlyList<InstalledPackInfo> Scan()
    {
        if (!Directory.Exists(_instancesRoot))
        {
            _logger.LogDebug(
                "Instances root does not exist: {Path}", _instancesRoot);
            return Array.Empty<InstalledPackInfo>();
        }

        var result = new List<InstalledPackInfo>();

        foreach (var dir in Directory.EnumerateDirectories(_instancesRoot)
            .OrderBy(d => d, StringComparer.Ordinal))
        {
            var info = TryReadInstance(dir);
            if (info is not null)
                result.Add(info);
        }

        result.Sort(static (a, b) =>
            string.CompareOrdinal(a.Name, b.Name));

        _logger.LogDebug(
            "Instances scan complete: {Count} valid pack(s) in {Path}",
            result.Count, _instancesRoot);

        return result;
    }

    // ------------------------------------------------------------------
    //  Один инстанс
    // ------------------------------------------------------------------

    private InstalledPackInfo? TryReadInstance(string instancePath)
    {
        var manifestPath = Path.Combine(instancePath, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            _logger.LogDebug(
                "Skipping instance without modlist.json: {Path}",
                instancePath);
            return null;
        }

        ModlistManifest manifest;
        try
        {
            manifest = ManifestJson.Load(manifestPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Skipping instance with malformed modlist.json: {Path}",
                manifestPath);
            return null;
        }

        return new InstalledPackInfo
        {
            Name = manifest.Meta.Name,
            Version = manifest.Meta.Version,
            Game = manifest.Meta.Game,
            GameVersion = manifest.Meta.GameVersion,
            CreatedAt = manifest.CreatedAt,
            InstancePath = instancePath,
            ManifestPath = manifestPath,
        };
    }
}
