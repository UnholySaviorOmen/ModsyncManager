// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Install;

namespace Modsync.Gui.Modules.Install.Services;

public sealed class InstallRunner : IInstallRunner
{
    private readonly InstallPipeline _pipeline;

    public InstallRunner(InstallPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<InstallSummary> RunAsync(
        string manifestPath,
        string? target,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        var input = InstallInputFactory.Create(
            manifestPath,
            target: target,
            parallelOptions: null);

        var output = await _pipeline.ExecuteAsync(input, ct, progress);

        return InstallSummaryBuilder.Build(output);
    }
}
