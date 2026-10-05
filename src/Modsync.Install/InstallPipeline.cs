// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest;
using Modsync.Core.Progress;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging;

namespace Modsync.Install;

/// <summary>
/// Оркестратор installer-а. Последовательно вызывает шаги 12.1–12.9.
///
/// Не содержит своей логики — только склейку шагов и передачу данных.
///
/// Порядок:
///   ReadManifestStep        → manifest, manifestPath
///   ResolveTargetStep       → instancePath, manifestPathInInstance
///   ValidateTargetStep      → (проверка)
///   BootstrapInstanceStep   → все пути инстанса
///   BootstrapMo2Step        → распакованный MO2/
///   SyncArchivesStep        → все архивы в downloads/
///   GenerateArchiveMetaStep → .meta для архивов в downloads/
///   ExecuteExtensionsStep   → extensions в MO2/  (12.9)
///   ExecuteExtrasStep       → extras в Stock Game/  (12.9)
///   SyncModsStep            → mods/ под манифест
///   GenerateMetaIniStep     → meta.ini
///   RegenerateProfileStep   → modlist.txt / plugins.txt / loadorder.txt
/// </summary>
public sealed class InstallPipeline
{
    private readonly ReadManifestStep _readManifest;
    private readonly ResolveTargetStep _resolveTarget;
    private readonly ValidateTargetStep _validateTarget;
    private readonly BootstrapInstanceStep _bootstrapInstance;
    private readonly BootstrapMo2Step _bootstrapMo2;
    private readonly SyncArchivesStep _syncArchives;
    private readonly GenerateArchiveMetaStep _generateArchiveMeta;
    private readonly ExecuteExtensionsStep _executeExtensions;
    private readonly ExecuteExtrasStep _executeExtras;
    private readonly SyncModsStep _syncMods;
    private readonly GenerateMetaIniStep _generateMetaIni;
    private readonly RegenerateProfileStep _regenerateProfile;
    private readonly ILogger<InstallPipeline> _logger;

    /// <summary>
    /// Имена шагов в порядке выполнения. Используются для StepProgress.StepName.
    /// Порядок и имена менять нельзя без причины — на них ориентируется GUI.
    /// </summary>
    private static readonly string[] StepNames =
    {
        "ReadManifest",
        "ResolveTarget",
        "ValidateTarget",
        "BootstrapInstance",
        "BootstrapMo2",
        "SyncArchives",
        "GenerateArchiveMeta",
        "ExecuteExtensions",
        "ExecuteExtras",
        "SyncMods",
        "GenerateMetaIni",
        "RegenerateProfile",
    };

    public InstallPipeline(
        ReadManifestStep readManifest,
        ResolveTargetStep resolveTarget,
        ValidateTargetStep validateTarget,
        BootstrapInstanceStep bootstrapInstance,
        BootstrapMo2Step bootstrapMo2,
        SyncArchivesStep syncArchives,
        GenerateArchiveMetaStep generateArchiveMeta,
        ExecuteExtensionsStep executeExtensions,
        ExecuteExtrasStep executeExtras,
        SyncModsStep syncMods,
        GenerateMetaIniStep generateMetaIni,
        RegenerateProfileStep regenerateProfile,
        ILogger<InstallPipeline> logger)
    {
        _readManifest = readManifest;
        _resolveTarget = resolveTarget;
        _validateTarget = validateTarget;
        _bootstrapInstance = bootstrapInstance;
        _bootstrapMo2 = bootstrapMo2;
        _syncArchives = syncArchives;
        _generateArchiveMeta = generateArchiveMeta;
        _executeExtensions = executeExtensions;
        _executeExtras = executeExtras;
        _syncMods = syncMods;
        _generateMetaIni = generateMetaIni;
        _regenerateProfile = regenerateProfile;
        _logger = logger;
    }

    public async Task<Output> ExecuteAsync(
        Input input,
        CancellationToken ct,
        IProgress<StepProgress>? progress = null)
    {
        _logger.LogInformation("=== Modsync Manager install started ===");
        _logger.LogInformation("Manifest: {Path}", input.ManifestPath);
        if (!string.IsNullOrWhiteSpace(input.Target))
            _logger.LogInformation("Target:   {Target}", input.Target);

        int totalSteps = StepNames.Length;

        // --- 1. ReadManifestStep ---
        progress?.Report(new StepProgress(1, totalSteps, StepNames[0]));
        var readManifestOutput = await _readManifest.ExecuteAsync(
            input.ManifestPath, ct);

        var manifest = readManifestOutput.Manifest;
        var manifestPath = readManifestOutput.ManifestPath;

        _logger.LogInformation(
            "Manifest: '{Name}' v{Version} ({Game})",
            manifest.Meta.Name,
            manifest.Meta.Version,
            manifest.Meta.Game);

        ct.ThrowIfCancellationRequested();

        // --- 2. ResolveTargetStep ---
        progress?.Report(new StepProgress(2, totalSteps, StepNames[1]));
        var resolveTargetOutput = await _resolveTarget.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = input.Target,
            }, ct);

        var instancePath = resolveTargetOutput.InstancePath;
        var usedExplicitTarget = !string.IsNullOrWhiteSpace(input.Target);

        ct.ThrowIfCancellationRequested();

        // --- 3. ValidateTargetStep ---
        progress?.Report(new StepProgress(3, totalSteps, StepNames[2]));
        var validateTargetOutput = await _validateTarget.ExecuteAsync(
            new ValidateTargetStep.Input
            {
                InstancePath = instancePath,
                UsedExplicitTarget = usedExplicitTarget,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 4. BootstrapInstanceStep ---
        progress?.Report(new StepProgress(4, totalSteps, StepNames[3]));
        var bootstrapInstanceOutput = await _bootstrapInstance.ExecuteAsync(
            new BootstrapInstanceStep.Input
            {
                InstancePath = validateTargetOutput.InstancePath,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 5. BootstrapMo2Step ---
        progress?.Report(new StepProgress(5, totalSteps, StepNames[4]));
        await _bootstrapMo2.ExecuteAsync(
            new BootstrapMo2Step.Input
            {
                InstancePath = bootstrapInstanceOutput.InstancePath,
                Manifest = manifest,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 6. SyncArchivesStep ---
        // Адаптер: SyncArchivesStep репортит (Completed, Total),
        // мы превращаем это в StepProgress.Detail для UI.
        var syncArchivesDetail = progress is null
            ? null
            : new Progress<(int Completed, int Total)>(p =>
                progress.Report(new StepProgress(
                    6, totalSteps, StepNames[5],
                    $"Downloading: {p.Completed} / {p.Total}")));

        progress?.Report(new StepProgress(6, totalSteps, StepNames[5]));
        var syncArchivesOutput = await _syncArchives.ExecuteAsync(
            new SyncArchivesStep.Input
            {
                Manifest = manifest,
                DownloadsPath = bootstrapInstanceOutput.DownloadsPath,
                ParallelOptions = input.ParallelOptions,
                DetailProgress = syncArchivesDetail,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 6a. Архивы по id (включая MO2-архив) ---
        var archivesById = BuildArchivesById(manifest);

        // --- 7. GenerateArchiveMetaStep ---
        progress?.Report(new StepProgress(7, totalSteps, StepNames[6]));
        var generateArchiveMetaOutput = await _generateArchiveMeta.ExecuteAsync(
            new GenerateArchiveMetaStep.Input
            {
                Manifest = manifest,
                DownloadsPath = bootstrapInstanceOutput.DownloadsPath,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 8. ExecuteExtensionsStep ---
        progress?.Report(new StepProgress(8, totalSteps, StepNames[7]));
        var executeExtensionsOutput = await _executeExtensions.ExecuteAsync(
            new ExecuteExtensionsStep.Input
            {
                InstancePath = bootstrapInstanceOutput.InstancePath,
                Manifest = manifest,
                DownloadsPath = bootstrapInstanceOutput.DownloadsPath,
                ArchivesById = archivesById,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 9. ExecuteExtrasStep ---
        progress?.Report(new StepProgress(9, totalSteps, StepNames[8]));
        var executeExtrasOutput = await _executeExtras.ExecuteAsync(
            new ExecuteExtrasStep.Input
            {
                InstancePath = bootstrapInstanceOutput.InstancePath,
                Manifest = manifest,
                DownloadsPath = bootstrapInstanceOutput.DownloadsPath,
                ArchivesById = archivesById,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 10. SyncModsStep ---
        // Адаптер: SyncModsStep репортит (Completed, Total) для Pass 1.
        var syncModsDetail = progress is null
            ? null
            : new Progress<(int Completed, int Total)>(p =>
                progress.Report(new StepProgress(
                    10, totalSteps, StepNames[9],
                    $"Syncing: {p.Completed} / {p.Total} mods")));

        progress?.Report(new StepProgress(10, totalSteps, StepNames[9]));
        var syncModsOutput = await _syncMods.ExecuteAsync(
            new SyncModsStep.Input
            {
                Manifest = manifest,
                DownloadsPath = bootstrapInstanceOutput.DownloadsPath,
                ModsPath = bootstrapInstanceOutput.ModsPath,
                ParallelOptions = input.ParallelOptions,
                ArchivesById = archivesById,
                DetailProgress = syncModsDetail,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 11. GenerateMetaIniStep ---
        progress?.Report(new StepProgress(11, totalSteps, StepNames[10]));
        var generateMetaIniOutput = await _generateMetaIni.ExecuteAsync(
            new GenerateMetaIniStep.Input
            {
                Manifest = manifest,
                ModsPath = bootstrapInstanceOutput.ModsPath,
            }, ct);

        ct.ThrowIfCancellationRequested();

        // --- 12. RegenerateProfileStep ---
        progress?.Report(new StepProgress(12, totalSteps, StepNames[11]));
        var regenerateProfileOutput = await _regenerateProfile.ExecuteAsync(
            new RegenerateProfileStep.Input
            {
                Manifest = manifest,
                ProfilesPath = bootstrapInstanceOutput.ProfilesPath,
            }, ct);

        _logger.LogInformation("=== Modsync Manager install finished ===");
        _logger.LogInformation("Instance: {Path}", bootstrapInstanceOutput.InstancePath);

        return new Output
        {
            InstancePath = bootstrapInstanceOutput.InstancePath,
            Manifest = manifest,
            BootstrapInstance = bootstrapInstanceOutput,
            SyncArchives = syncArchivesOutput,
            GenerateArchiveMeta = generateArchiveMetaOutput,
            ExecuteExtensions = executeExtensionsOutput,
            ExecuteExtras = executeExtrasOutput,
            SyncMods = syncModsOutput,
            GenerateMetaIni = generateMetaIniOutput,
            RegenerateProfile = regenerateProfileOutput,
        };
    }

    /// <summary>
    /// Строит map id → ArchiveEntry.
    /// Включает manifest.Archives[] И manifest.Mo2.Archive — потому что
    /// директивы extensions/extras могут ссылаться на MO2-архив.
    /// </summary>
    private static IReadOnlyDictionary<string, ArchiveEntry> BuildArchivesById(
        ModlistManifest manifest)
    {
        var result = new Dictionary<string, ArchiveEntry>(
            manifest.Archives.Count + 1, StringComparer.Ordinal);

        foreach (var archive in manifest.Archives)
            result[archive.Id] = archive;

        // MO2-архив. Не должен конфликтовать по id с manifest.Archives[].
        result[manifest.Mo2.Archive.Id] = manifest.Mo2.Archive;

        return result;
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        /// <summary>Путь к modlist.json (откуда угодно).</summary>
        public required string ManifestPath { get; init; }

        /// <summary>
        /// --target &lt;dir&gt;, escape-hatch. Если null — auto-resolve
        /// в &lt;exeDir&gt;/Instances/&lt;normalize(meta.name)&gt;/.
        /// </summary>
        public string? Target { get; init; }

        public required ParallelOptions ParallelOptions { get; init; }
    }

    public sealed class Output
    {
        public required string InstancePath { get; init; }
        public required ModlistManifest Manifest { get; init; }

        public required BootstrapInstanceStep.Output BootstrapInstance { get; init; }
        public required SyncArchivesStep.Output SyncArchives { get; init; }
        public required GenerateArchiveMetaStep.Output GenerateArchiveMeta { get; init; }
        public required ExecuteExtensionsStep.Output ExecuteExtensions { get; init; }
        public required ExecuteExtrasStep.Output ExecuteExtras { get; init; }
        public required SyncModsStep.Output SyncMods { get; init; }
        public required GenerateMetaIniStep.Output GenerateMetaIni { get; init; }
        public required RegenerateProfileStep.Output RegenerateProfile { get; init; }
    }
}
