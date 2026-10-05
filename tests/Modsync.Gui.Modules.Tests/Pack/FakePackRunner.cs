// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Gui.Modules.Pack.Services;
using Modsync.Pack;
using Modsync.Pack.Models;

namespace Modsync.Gui.Modules.Tests.Pack;

public sealed class FakePackRunner : IPackRunner
{
    public PackSummary? ResultToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public List<StepProgress> ReportedProgress { get; } = new();
    public CancellationToken LastToken { get; private set; }
    public PackConfigBuilderInput? LastConfigBuilderInput { get; private set; }

    public async Task<PackSummary> RunFromConfigBuilderAsync(
        PackConfigBuilderInput input,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        LastConfigBuilderInput = input;
        LastToken = ct;

        if (Gate is not null)
        {
            using var reg = ct.Register(() => Gate.TrySetCanceled(ct));
            await Gate.Task;
        }

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        if (ResultToReturn is null)
            throw new InvalidOperationException("FakePackRunner.ResultToReturn is null.");

        return ResultToReturn;
    }

    public static PackSummary MakeSummary(
        string name = "Test Pack",
        string version = "1.0.0",
        string game = "skyrimspecialedition",
        string instancePath = "/test/instance",
        int modsScanned = 0,
        int filesScanned = 0,
        int directivesTotal = 0,
        int directivesFromArchive = 0,
        int archivesResolved = 0,
        int archivesUnresolved = 0,
        int unmatchedFiles = 0,
        int metaIniCount = 0,
        int manifestMods = 0,
        int manifestArchives = 0,
        int manifestExtensions = 0,
        int manifestExtras = 0,
        string? unmatchedWrittenTo = null,
        string manifestPath = "/test/instance/__ModsyncManager_Output/modlist.json")
    {
        return new PackSummary
        {
            Name = name,
            Version = version,
            Game = game,
            InstancePath = instancePath,
            ModsTotal = modsScanned,
            PluginsTotal = 0,
            LoadorderTotal = 0,
            ArchivesResolved = archivesResolved,
            ArchivesUnresolved = archivesUnresolved,
            ModsScanned = modsScanned,
            FilesScanned = filesScanned,
            DirectivesTotal = directivesTotal,
            DirectivesFromArchive = directivesFromArchive,
            UnmatchedFiles = unmatchedFiles,
            MetaIniCount = metaIniCount,
            ManifestMods = manifestMods,
            ManifestArchives = manifestArchives,
            ManifestExtensions = manifestExtensions,
            ManifestExtras = manifestExtras,
            UnmatchedWrittenTo = unmatchedWrittenTo,
            ManifestPath = manifestPath,
        };
    }
}
