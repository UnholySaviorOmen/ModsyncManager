// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Pack;

namespace Modsync.Gui.Modules.Pack.Services;

public sealed class PackRunner : IPackRunner
{
    private readonly PackPipeline _pipeline;

    public PackRunner(PackPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<PackSummary> RunAsync(
        string configPath,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        var input = PackInputFactory.Create(configPath);

        var result = await _pipeline.ExecuteAsync(input, ct, progress);

        return PackSummaryBuilder.Build(result);
    }
}
