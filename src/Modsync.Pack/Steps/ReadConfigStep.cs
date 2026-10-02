// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Pack;
using Modsync.Core.Validation;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Читает modsyncmanager-pack.json, валидирует через PackConfigValidator.
/// Возвращает PackConfig или бросает InvalidOperationException с полным списком ошибок.
/// </summary>
public sealed class ReadConfigStep : IStep<string, PackConfig>
{
    private readonly ILogger<ReadConfigStep> _logger;

    public ReadConfigStep(ILogger<ReadConfigStep> logger)
    {
        _logger = logger;
    }

    public async Task<PackConfig> ExecuteAsync(string configPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path must be non-empty.", nameof(configPath));

        if (!File.Exists(configPath))
            throw new FileNotFoundException(
                $"modsyncmanager-pack.json not found: {configPath}", configPath);

        _logger.LogInformation("Reading config: {Path}", configPath);

        PackConfig config;
        try
        {
            config = await PackConfigJson.LoadAsync(configPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to parse modsyncmanager-pack.json: {ex.Message}", ex);
        }

        var validation = PackConfigValidator.Validate(config);
        if (!validation.IsValid)
        {
            var msg = "modsyncmanager-pack.json is invalid:" + Environment.NewLine +
                      string.Join(Environment.NewLine, validation.Errors.Select(e => "  - " + e));
            throw new InvalidOperationException(msg);
        }

        _logger.LogInformation(
            "Config OK: {Name} v{Version} ({Game})",
            config.Meta.Name, config.Meta.Version, config.Meta.Game);

        return config;
    }
}
