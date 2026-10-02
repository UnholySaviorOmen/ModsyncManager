// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Mo2;
using Modsync.Platform.MO2.Writers;
using Microsoft.Extensions.Logging;

using Mo2ModlistEntry = Modsync.Core.Models.Mo2.ModlistEntry;
using Mo2PluginEntry = Modsync.Core.Models.Mo2.PluginEntry;

namespace Modsync.Install.Steps;

/// <summary>
/// Генерирует файлы профиля MO2 из манифеста:
///   - profiles/&lt;Profile&gt;/modlist.txt
///   - profiles/&lt;Profile&gt;/plugins.txt
///   - profiles/&lt;Profile&gt;/loadorder.txt
///
/// Профиль берётся из manifest.Mo2.Profile.
///
/// Порядок модов:
///   manifest.Mods сортируется по Order по возрастанию.
///   Order = 0 — первая строка в modlist.txt (верх оригинала).
///   Order = N — последняя строка.
///   Никаких Reverse() — MO2 хранит modlist.txt в том же порядке,
///   в котором мы его записали: сверху вниз соответствует
///   порядку строк в оригинальном modlist.txt. Приоритет в MO2 —
///   обратный порядку в файле (верх = ниже приоритет), но это
///   забота MO2, а не installer-а.
///
/// Сепараторы (#...) остаются: они уже в manifest.Mods с Enabled = false,
/// записываются как обычные disabled-строки.
///
/// Файлы перезаписываются (источник правды — манифест).
/// </summary>
public sealed class RegenerateProfileStep
    : IStep<RegenerateProfileStep.Input, RegenerateProfileStep.Output>
{
    private const string ModlistFileName = "modlist.txt";
    private const string PluginsFileName = "plugins.txt";
    private const string LoadorderFileName = "loadorder.txt";

    private readonly ILogger<RegenerateProfileStep> _logger;

    public RegenerateProfileStep(ILogger<RegenerateProfileStep> logger)
    {
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var profilesPath = Path.GetFullPath(input.ProfilesPath);
        var profileName = input.Manifest.Mo2.Profile;

        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new InvalidOperationException(
                "manifest.Mo2.Profile is empty. This is a manifest error.");
        }

        var profileDir = Path.Combine(profilesPath, profileName);
        Directory.CreateDirectory(profileDir);

        _logger.LogInformation(
            "Regenerating profile '{Profile}' in {Path}",
            profileName, profileDir);

        // --- modlist.txt
        // Order = 0 → верх, Order = N → низ. Никаких Reverse().
        var modEntries = input.Manifest.Mods
            .OrderBy(m => m.Order)
            .Select(m => new Mo2ModlistEntry(m.Name, m.Enabled))
            .ToList();

        var modlistFile = new ModlistFile { Entries = modEntries };
        var modlistPath = Path.Combine(profileDir, ModlistFileName);
        ModlistWriter.WriteFile(modlistPath, modlistFile);

        // --- plugins.txt
        var pluginEntries = input.Manifest.Plugins
            .Select(p => new Mo2PluginEntry(p.Name, p.Enabled))
            .ToList();

        var pluginsFile = new PluginsFile { Entries = pluginEntries };
        var pluginsPath = Path.Combine(profileDir, PluginsFileName);
        PluginsWriter.WriteFile(pluginsPath, pluginsFile);

        // --- loadorder.txt
        var loadorderFile = new LoadorderFile
        {
            Plugins = input.Manifest.Loadorder.ToList(),
        };
        var loadorderPath = Path.Combine(profileDir, LoadorderFileName);
        LoadorderWriter.WriteFile(loadorderPath, loadorderFile);

        _logger.LogInformation(
            "Profile regenerated: {Mods} mods, {Plugins} plugins, {Loadorder} loadorder entries",
            modEntries.Count, pluginEntries.Count, loadorderFile.Plugins.Count);

        return Task.FromResult(new Output
        {
            ProfilePath = profileDir,
            ModlistPath = modlistPath,
            PluginsPath = pluginsPath,
            LoadorderPath = loadorderPath,
            ModlistCount = modEntries.Count,
            PluginsCount = pluginEntries.Count,
            LoadorderCount = loadorderFile.Plugins.Count,
        });
    }

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
        public required string ProfilesPath { get; init; }
    }

    public sealed class Output
    {
        public required string ProfilePath { get; init; }
        public required string ModlistPath { get; init; }
        public required string PluginsPath { get; init; }
        public required string LoadorderPath { get; init; }
        public required int ModlistCount { get; init; }
        public required int PluginsCount { get; init; }
        public required int LoadorderCount { get; init; }
    }
}
