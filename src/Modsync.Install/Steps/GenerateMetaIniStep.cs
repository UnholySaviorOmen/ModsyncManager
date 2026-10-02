// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Platform.MO2.Writers;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Reconcile meta.ini в mods/&lt;ModName&gt;/.ini.
///
/// Логика:
///   Для каждого mod из manifest.Mods:
///     - если папка содержит [NoDelete] → пропустить.
///     - если mods[i].meta != null → записать meta.ini (перезапись всегда).
///     - если mods[i].meta == null и файла нет → ничего.
///     - если mods[i].meta == null и файл есть → удалить.
///
/// Работаем только с mods/&lt;Name&gt;/meta.ini (в корне папки мода).
/// meta.ini внутри подпапок (например, fomod/meta.ini) — не трогаем.
///
/// Не параллелится: операция дешёвая (запись маленького файла).
///
/// Идемпотентен: повторный запуск даёт то же состояние.
/// </summary>
public sealed class GenerateMetaIniStep
    : IStep<GenerateMetaIniStep.Input, GenerateMetaIniStep.Output>
{
    private const string NoDeleteMarker = "[NoDelete]";
    private const string MetaIniFileName = "meta.ini";

    private readonly ILogger<GenerateMetaIniStep> _logger;

    public GenerateMetaIniStep(ILogger<GenerateMetaIniStep> logger)
    {
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var modsPath = Path.GetFullPath(input.ModsPath);

        _logger.LogInformation(
            "Generating meta.ini for {Count} mods in {ModsPath}",
            input.Manifest.Mods.Count, modsPath);

        var written = new List<string>();
        var deleted = new List<string>();

        foreach (var mod in input.Manifest.Mods)
        {
            ct.ThrowIfCancellationRequested();

            if (mod.Name.Contains(NoDeleteMarker, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Mod '{Name}' contains [NoDelete] — skipping meta.ini",
                    mod.Name);
                continue;
            }

            var modDir = Path.Combine(modsPath, mod.Name);
            if (!Directory.Exists(modDir))
            {
                // SyncModsStep должен был создать. Но если нет —
                // meta.ini писать некуда. Это контрактное нарушение,
                // но безопаснее пропустить, чем упасть.
                _logger.LogWarning(
                    "Mod directory not found for '{Name}' — skipping meta.ini " +
                    "(SyncModsStep should have created it)",
                    mod.Name);
                continue;
            }

            var metaIniPath = Path.Combine(modDir, MetaIniFileName);

            if (mod.Meta is not null)
            {
                MetaIniWriter.WriteFile(metaIniPath, mod.Meta);
                written.Add(mod.Name);
                _logger.LogDebug("Mod '{Name}': wrote meta.ini", mod.Name);
            }
            else
            {
                if (File.Exists(metaIniPath))
                {
                    try
                    {
                        File.Delete(metaIniPath);
                        deleted.Add(mod.Name);
                        _logger.LogDebug(
                            "Mod '{Name}': deleted meta.ini (not in manifest)",
                            mod.Name);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to delete meta.ini in '{modDir}': " +
                            $"{ex.Message}. Close Mod Organizer / game if running.",
                            ex);
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Mod '{Name}': no meta.ini and none in manifest — nothing to do",
                        mod.Name);
                }
            }
        }

        _logger.LogInformation(
            "meta.ini sync complete: {Written} written, {Deleted} deleted",
            written.Count, deleted.Count);

        return Task.FromResult(new Output
        {
            Written = written,
            Deleted = deleted,
        });
    }

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
        public required string ModsPath { get; init; }
    }

    public sealed class Output
    {
        public required IReadOnlyList<string> Written { get; init; }
        public required IReadOnlyList<string> Deleted { get; init; }
    }
}
