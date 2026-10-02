// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Pack;
using Modsync.Platform.MO2.Readers;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Читает инстанс MO2:
///  - Определяет пути (MO2/, downloads/, mods/, profiles/, Stock Game/, __ModsyncManager_Output/).
///  - Проверяет, что инстанс существует и содержит нужные папки.
///  - Читает modlist.txt, plugins.txt, loadorder.txt из profiles/{profile}/.
/// Возвращает InstanceSnapshot.
/// </summary>
public sealed class ReadInstanceStep : IStep<ReadInstanceStep.Input, InstanceSnapshot>
{
    private readonly ILogger<ReadInstanceStep> _logger;

    public ReadInstanceStep(ILogger<ReadInstanceStep> logger)
    {
        _logger = logger;
    }

    public Task<InstanceSnapshot> ExecuteAsync(Input input, CancellationToken ct)
    {
        var configDir = Path.GetDirectoryName(input.ConfigPath)
            ?? throw new InvalidOperationException(
                $"Cannot determine directory of config: {input.ConfigPath}");

        var instancePath = Path.GetFullPath(
            Path.Combine(configDir, input.Config.Instance.Path));

        if (!Directory.Exists(instancePath))
            throw new DirectoryNotFoundException(
                $"Instance directory not found: {instancePath} " +
                $"(instance.path = '{input.Config.Instance.Path}')");

        var mo2Path = Path.Combine(instancePath, "MO2");
        if (!Directory.Exists(mo2Path))
            throw new DirectoryNotFoundException(
                $"MO2/ not found in instance: {mo2Path}");

        var downloadsPath = Path.Combine(mo2Path, "downloads");
        var modsPath = Path.Combine(mo2Path, "mods");
        var profilesPath = Path.Combine(mo2Path, "profiles");
        var stockGamePath = Path.Combine(instancePath, "Stock Game");

        // __ModsyncManager_Output живёт в корне инстанса, вне mods/ и MO2/.
        // Здесь только вычисляется путь; создаёт и очищает его MatchStep.
        var modsyncOutputPath = Path.Combine(instancePath, "__ModsyncManager_Output");

        if (!Directory.Exists(downloadsPath))
            throw new DirectoryNotFoundException($"downloads/ not found: {downloadsPath}");

        if (!Directory.Exists(modsPath))
            throw new DirectoryNotFoundException($"mods/ not found: {modsPath}");

        if (!Directory.Exists(profilesPath))
            throw new DirectoryNotFoundException($"profiles/ not found: {profilesPath}");

        var profileName = input.Config.Mo2.Profile;
        var profilePath = Path.Combine(profilesPath, profileName);

        if (!Directory.Exists(profilePath))
            throw new DirectoryNotFoundException(
                $"Profile not found: {profilePath} (mo2.profile = '{profileName}')");

        var modlistPath = Path.Combine(profilePath, "modlist.txt");
        var pluginsPath = Path.Combine(profilePath, "plugins.txt");
        var loadorderPath = Path.Combine(profilePath, "loadorder.txt");

        _logger.LogInformation("Reading instance: {Path}", instancePath);
        _logger.LogInformation("Reading profile: {Profile}", profileName);

        var modlist = ModlistReader.ReadFile(modlistPath);
        var plugins = PluginsReader.ReadFile(pluginsPath);
        var loadorder = LoadorderReader.ReadFile(loadorderPath);

        _logger.LogInformation(
            "Instance snapshot: {Mods} mods, {Plugins} plugins, {Loadorder} load order entries",
            modlist.Entries.Count, plugins.Entries.Count, loadorder.Plugins.Count);

        return Task.FromResult(new InstanceSnapshot
        {
            InstancePath = instancePath,
            Mo2Path = mo2Path,
            DownloadsPath = downloadsPath,
            ModsPath = modsPath,
            ProfilesPath = profilesPath,
            StockGamePath = stockGamePath,
            ModsyncOutputPath = modsyncOutputPath,
            Modlist = modlist,
            Plugins = plugins,
            Loadorder = loadorder,
        });
    }

    public sealed class Input
    {
        public required string ConfigPath { get; init; }
        public required PackConfig Config { get; init; }
    }
}
