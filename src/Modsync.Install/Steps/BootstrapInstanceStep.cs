// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Создаёт структуру папок инстанса MO2.
///
/// Что делает:
///   - проверяет, что instancePath существует (ResolveTargetStep обязан был создать);
///   - проверяет, что instancePath/modlist.json существует;
///   - создаёт MO2/ + все стандартные подпапки + Stock Game/;
///   - возвращает все пути следующим шагам.
///
/// Что НЕ делает:
///   - не трогает modlist.json;
///   - не создаёт ModOrganizer.exe, portable.txt, ModOrganizer.ini
///     (это BootstrapMo2Step и ConfigureMo2Step);
///   - не создаёт __ModsyncManager_Output/ (это каталог автора, installer его не знает);
///   - не чистит mods/ (это SyncModsStep, reconcile);
///   - не скачивает архивы (это SyncArchivesStep).
///
/// Идемпотентен: повторный запуск не падает.
/// Логирует только те папки, которые реально создал (не «уже есть»).
/// </summary>
public sealed class BootstrapInstanceStep
    : IStep<BootstrapInstanceStep.Input, BootstrapInstanceStep.Output>
{
    private const string Mo2DirName = "MO2";
    private const string StockGameDirName = "Stock Game";
    private const string DownloadsDirName = "downloads";
    private const string ModsDirName = "mods";
    private const string ProfilesDirName = "profiles";
    private const string PluginsDirName = "plugins";
    private const string ToolsDirName = "tools";
    private const string ManifestFileName = "modlist.json";

    private readonly ILogger<BootstrapInstanceStep> _logger;

    public BootstrapInstanceStep(ILogger<BootstrapInstanceStep> logger)
    {
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(input.InstancePath))
            throw new ArgumentException(
                "InstancePath must be non-empty.", nameof(input));

        var instancePath = Path.GetFullPath(input.InstancePath);

        // Контрактная проверка: ResolveTargetStep обязан был создать instancePath.
        if (!Directory.Exists(instancePath))
        {
            throw new DirectoryNotFoundException(
                $"Instance path does not exist: {instancePath}. " +
                $"ResolveTargetStep should have created it. " +
                $"This is a pipeline contract violation, not a user error.");
        }

        // Контрактная проверка: ResolveTargetStep обязан был скопировать манифест.
        var manifestInInstance = Path.Combine(instancePath, ManifestFileName);
        if (!File.Exists(manifestInInstance))
        {
            throw new FileNotFoundException(
                $"Manifest not found in instance: {manifestInInstance}. " +
                $"ResolveTargetStep should have copied it. " +
                $"This is a pipeline contract violation, not a user error.",
                manifestInInstance);
        }

        _logger.LogInformation(
            "Bootstrapping instance structure: {InstancePath}", instancePath);

        var mo2Path = Path.Combine(instancePath, Mo2DirName);
        var stockGamePath = Path.Combine(instancePath, StockGameDirName);
        var downloadsPath = Path.Combine(mo2Path, DownloadsDirName);
        var modsPath = Path.Combine(mo2Path, ModsDirName);
        var profilesPath = Path.Combine(mo2Path, ProfilesDirName);
        var pluginsPath = Path.Combine(mo2Path, PluginsDirName);
        var toolsPath = Path.Combine(mo2Path, ToolsDirName);

        int createdCount = 0;
        createdCount += EnsureDirectory(mo2Path);
        createdCount += EnsureDirectory(downloadsPath);
        createdCount += EnsureDirectory(modsPath);
        createdCount += EnsureDirectory(profilesPath);
        createdCount += EnsureDirectory(pluginsPath);
        createdCount += EnsureDirectory(toolsPath);
        createdCount += EnsureDirectory(stockGamePath);

        _logger.LogInformation(
            "Bootstrap complete: {Created} folder(s) created, " +
            "instance structure ready at {InstancePath}",
            createdCount, instancePath);

        return Task.FromResult(new Output
        {
            InstancePath = instancePath,
            Mo2Path = mo2Path,
            DownloadsPath = downloadsPath,
            ModsPath = modsPath,
            ProfilesPath = profilesPath,
            PluginsPath = pluginsPath,
            ToolsPath = toolsPath,
            StockGamePath = stockGamePath,
            ManifestPathInInstance = manifestInInstance,
        });
    }

    /// <summary>
    /// Создаёт папку, если её нет. Возвращает 1, если создал, 0, если уже была.
    /// </summary>
    private int EnsureDirectory(string path)
    {
        if (Directory.Exists(path))
            return 0;

        Directory.CreateDirectory(path);
        _logger.LogInformation("Created: {Path}", path);
        return 1;
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        /// <summary>
        /// Целевая папка инстанса. Должна существовать и содержать modlist.json —
        /// это гарантирует ResolveTargetStep, вызванный до BootstrapInstanceStep.
        /// </summary>
        public required string InstancePath { get; init; }
    }

    public sealed class Output
    {
        public required string InstancePath { get; init; }
        public required string Mo2Path { get; init; }
        public required string DownloadsPath { get; init; }
        public required string ModsPath { get; init; }
        public required string ProfilesPath { get; init; }
        public required string PluginsPath { get; init; }
        public required string ToolsPath { get; init; }
        public required string StockGamePath { get; init; }
        public required string ManifestPathInInstance { get; init; }
    }
}
