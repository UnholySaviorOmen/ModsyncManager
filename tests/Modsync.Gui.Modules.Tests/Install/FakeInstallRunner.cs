// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Gui.Modules.Install.Services;
using Modsync.Install;

namespace Modsync.Gui.Modules.Tests.Install;

public sealed class FakeInstallRunner : IInstallRunner
{
    public InstallSummary? ResultToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public List<StepProgress> ReportedProgress { get; } = new();
    public CancellationToken LastToken { get; private set; }
    public string? LastManifestPath { get; private set; }
    public string? LastTarget { get; private set; }

    public async Task<InstallSummary> RunAsync(
        string manifestPath,
        string? target,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        LastManifestPath = manifestPath;
        LastTarget = target;
        LastToken = ct;

        if (Gate is not null)
        {
            using var reg = ct.Register(() => Gate.TrySetCanceled(ct));
            await Gate.Task;
        }

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        if (ResultToReturn is null)
            throw new InvalidOperationException("FakeInstallRunner.ResultToReturn is null.");

        return ResultToReturn;
    }

    public static InstallSummary MakeSummary(
        string name = "Test Pack",
        string version = "1.0.0",
        string game = "skyrimspecialedition",
        string instancePath = "/test/instance",
        int modsCreated = 0,
        int modsSkipped = 0,
        int archivesDownloaded = 0,
        int archivesPresent = 0,
        int metaIniWritten = 0,
        int archiveMetaWritten = 0,
        int archiveMetaSkipped = 0)
    {
        return new InstallSummary
        {
            Name = name,
            Version = version,
            Game = game,
            InstancePath = instancePath,
            ManifestPathInInstance = Path.Combine(instancePath, "modlist.json"),
            ArchivesAlreadyPresent = archivesPresent,
            ArchivesDownloaded = archivesDownloaded,
            ArchivesSkipped = 0,
            ModsCreated = modsCreated,
            ModsRecreated = 0,
            ModsSkipped = modsSkipped,
            ModsDeleted = 0,
            MetaIniWritten = metaIniWritten,
            MetaIniDeleted = 0,
            ArchiveMetaWritten = archiveMetaWritten,
            ArchiveMetaSkipped = archiveMetaSkipped,
            ExtensionsWritten = 0,
            ExtensionsSkipped = 0,
            ExtrasWritten = 0,
            ExtrasSkipped = 0,
            ProfileMods = 0,
            ProfilePlugins = 0,
            ProfileLoadorder = 0,
        };
    }
}
