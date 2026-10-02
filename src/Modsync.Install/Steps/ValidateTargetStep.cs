// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Проверяет, что целевая папка пригодна для установки.
///
/// Четыре категории проверок:
///   1. Не корень диска и не системная папка (Windows, Program Files,
///      ProgramData, Users без имени пользователя).
///   2. Не совпадает с папкой, где лежит ModsyncManager.Install.exe, и не
///      является её родителем (иначе можно скопировать себя в себя).
///   3. Есть права на запись в целевую папку.
///   4. &lt;exeDir&gt;/Instances доступен для записи (только если --target не задан).
///
/// Installer — тупой исполнитель. Он не «исправляет» проблему, а
/// отказывается работать и говорит, что не так.
/// </summary>
public sealed class ValidateTargetStep : IStep<ValidateTargetStep.Input, ValidateTargetStep.Output>
{
    private static readonly string[] SystemFolderNames =
    {
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
    };

    private readonly ILogger<ValidateTargetStep> _logger;

    public ValidateTargetStep(ILogger<ValidateTargetStep> logger)
    {
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var instancePath = Path.GetFullPath(input.InstancePath);
        var exeDir = Path.GetFullPath(AppContext.BaseDirectory);

        _logger.LogInformation(
            "Validating target: {InstancePath}", instancePath);

        RejectRootAndSystemFolders(instancePath);
        RejectExeFolderAndParents(instancePath, exeDir);
        EnsureWriteAccess(instancePath);

        if (!input.UsedExplicitTarget)
        {
            var instancesRoot = Path.Combine(exeDir, "Instances");
            Directory.CreateDirectory(instancesRoot);
            EnsureWriteAccess(instancesRoot);
        }

        _logger.LogInformation("Target is valid: {InstancePath}", instancePath);

        return Task.FromResult(new Output
        {
            InstancePath = instancePath,
        });
    }

    // ------------------------------------------------------------------
    //  Проверки
    // ------------------------------------------------------------------

    private static void RejectRootAndSystemFolders(string instancePath)
    {
        // Корень диска: C:\, D:\ и т.д.
        var root = Path.GetPathRoot(instancePath);
        if (root is not null &&
            string.Equals(
                instancePath.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Target path is a drive root: {instancePath}. " +
                $"Use a subfolder, e.g. <ModsyncManager>\\Instances\\<PackName>\\.");
        }

        // Системные папки.
        foreach (var systemName in SystemFolderNames)
        {
            var systemPath = Path.Combine(
                Path.GetPathRoot(instancePath) ?? "C:\\", systemName);

            if (IsSubPathOf(instancePath, systemPath))
            {
                throw new InvalidOperationException(
                    $"Target path is inside a system folder " +
                    $"('{systemPath}'): {instancePath}.");
            }
        }

        // C:\Users\ без имени пользователя.
        var usersPath = Path.Combine(
            Path.GetPathRoot(instancePath) ?? "C:\\", "Users");

        if (string.Equals(
                instancePath.TrimEnd(Path.DirectorySeparatorChar),
                usersPath.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Target path must not be the Users folder itself: {instancePath}.");
        }
    }

    private static void RejectExeFolderAndParents(string instancePath, string exeDir)
    {
        // instancePath == exeDir?
        if (string.Equals(
                instancePath.TrimEnd(Path.DirectorySeparatorChar),
                exeDir.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Target path is the ModsyncManager executable folder itself: {instancePath}. " +
                $"Use <ModsyncManager>\\Instances\\<PackName>\\ instead.");
        }

        // instancePath — родитель exeDir?
        if (IsSubPathOf(exeDir, instancePath))
        {
            throw new InvalidOperationException(
                $"Target path is a parent of the ModsyncManager executable folder: {instancePath}. " +
                $"Refusing to install into a folder that contains ModsyncManager itself.");
        }
    }

    private static void EnsureWriteAccess(string folderPath)
    {
        Directory.CreateDirectory(folderPath);

        var probePath = Path.Combine(
            folderPath, ".modsyncmanager-write-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            File.WriteAllText(probePath, "");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"No write access to '{folderPath}': {ex.Message}. " +
                $"If ModsyncManager is inside Program Files, move it to a user-writable " +
                $"location, e.g. C:\\Games\\ModsyncManager\\.", ex);
        }
        finally
        {
            try { File.Delete(probePath); } catch { }
        }
    }

    private static bool IsSubPathOf(string childPath, string parentPath)
    {
        var child = childPath.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var parent = parentPath.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required string InstancePath { get; init; }

        /// <summary>true, если пользователь передал --target.</summary>
        public required bool UsedExplicitTarget { get; init; }
    }

    public sealed class Output
    {
        public required string InstancePath { get; init; }
    }
}
