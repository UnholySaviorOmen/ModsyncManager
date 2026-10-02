// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Validation;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Определяет целевую папку инстанса.
///
/// Логика:
///   --target задан  → instancePath = Path.GetFullPath(--target)
///   --target не задан → instancePath = &lt;exeDir&gt;/Instances/&lt;normalize(meta.name)&gt;
///
/// Затем копирует манифест в &lt;instancePath&gt;/modlist.json.
///
/// Почему копирование здесь, а не в ReadManifestStep:
///   ReadManifestStep не знает meta.name — он только читает файл.
///   ResolveTargetStep — шаг, который решает, куда раскладывать файлы.
///   Копирование манифеста — часть этого решения.
///
/// Перезапись &lt;instancePath&gt;/modlist.json — без --force.
/// Installer идемпотентен: если манифест тот же — ничего не меняется.
/// Если манифест другой — это обновление сборки, и новый манифест
/// становится источником правды.
/// </summary>
public sealed class ResolveTargetStep : IStep<ResolveTargetStep.Input, ResolveTargetStep.Output>
{
    private const string ManifestFileName = "modlist.json";
    private const string InstancesDirName = "Instances";

    private readonly ILogger<ResolveTargetStep> _logger;

    public ResolveTargetStep(ILogger<ResolveTargetStep> logger)
    {
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Перепроверяем meta.name. Packer уже делал это, но installer
        // не доверяет внешнему файлу.
        var nameResult = NameValidator.Validate(input.Manifest.Meta.Name);
        if (!nameResult.IsValid)
        {
            throw new InvalidOperationException(
                "Manifest meta.name is invalid: " +
                string.Join("; ", nameResult.Errors));
        }

        string instancePath;

        if (!string.IsNullOrWhiteSpace(input.Target))
        {
            instancePath = Path.GetFullPath(input.Target);
            _logger.LogInformation(
                "Using --target: {InstancePath}", instancePath);
        }
        else
        {
            var instancesRoot = Path.Combine(
                AppContext.BaseDirectory, InstancesDirName);

            instancePath = Path.Combine(
                instancesRoot, input.Manifest.Meta.Name);

            _logger.LogInformation(
                "Auto-resolved instance path: {InstancePath}", instancePath);
        }

        Directory.CreateDirectory(instancePath);

        var targetManifestPath = Path.Combine(instancePath, ManifestFileName);
        CopyManifestIfNeeded(input.ManifestPath, targetManifestPath);

        return Task.FromResult(new Output
        {
            InstancePath = instancePath,
            ManifestPathInInstance = targetManifestPath,
            Manifest = input.Manifest,
        });
    }

    private void CopyManifestIfNeeded(string sourcePath, string targetPath)
    {
        var sourceFull = Path.GetFullPath(sourcePath);
        var targetFull = Path.GetFullPath(targetPath);

        if (string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Manifest already in place: {Path}", targetFull);
            return;
        }

        try
        {
            File.Copy(sourceFull, targetFull, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to copy manifest to '{targetFull}': {ex.Message}", ex);
        }

        _logger.LogInformation(
            "Manifest copied: {Source} → {Target}", sourceFull, targetFull);
    }

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
        public required string ManifestPath { get; init; }
        public string? Target { get; init; }
    }

    public sealed class Output
    {
        public required string InstancePath { get; init; }
        public required string ManifestPathInInstance { get; init; }
        public required ModlistManifest Manifest { get; init; }
    }
}
