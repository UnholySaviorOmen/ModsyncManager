// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Pack;
using Modsync.Core.Progress;
using Modsync.Pack;
using Modsync.Pack.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.Services;

public sealed class PackRunner : IPackRunner
{
    private const string TempConfigName = "modsyncmanager-pack.temp.json";

    private readonly PackPipeline _pipeline;
    private readonly PackConfigBuilder _builder;
    private readonly ILogger<PackRunner> _logger;

    public PackRunner(
        PackPipeline pipeline,
        PackConfigBuilder builder,
        ILogger<PackRunner> logger)
    {
        _pipeline = pipeline;
        _builder = builder;
        _logger = logger;
    }

    public async Task<PackSummary> RunFromConfigBuilderAsync(
        PackConfigBuilderInput input,
        IProgress<StepProgress> progress,
        CancellationToken ct)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var config = _builder.Build(input);
        var tempPath = Path.Combine(input.InstancePath, TempConfigName);

        _logger.LogInformation(
            "Running pack from generated config: {Path}", tempPath);

        try
        {
            var json = PackConfigJson.Serialize(config);
            await File.WriteAllTextAsync(tempPath, json, ct);

            var packInput = PackInputFactory.Create(tempPath);
            var result = await _pipeline.ExecuteAsync(packInput, ct, progress);

            return PackSummaryBuilder.Build(result);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to delete temp config: {Path}", tempPath);
            }
        }
    }
}
