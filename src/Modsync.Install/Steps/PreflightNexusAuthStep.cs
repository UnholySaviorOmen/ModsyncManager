// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Pre-flight проверка: если в манифесте есть nexus-источники
/// и API-ключ не установлен — падаем сразу, с понятным сообщением.
///
/// Зачем: иначе SyncArchivesStep попытается скачать N архивов
/// параллельно, каждый упадёт с NexusAuthenticationException,
/// и пользователь получит невнятную ошибку «SkyUI failed».
/// Preflight даёт мгновенный ответ: «Nexus API key is not set,
/// открой Settings → Nexus».
///
/// Не делает сетевых запросов. Только проверяет наличие ключа.
/// Если ключ есть — пропускает. Если нет — бросает.
///
/// Если в манифесте нет ни одного nexus-источника — no-op
/// (ключ не нужен).
///
/// MO2-архив тоже проверяется: если manifest.Mo2.Archive.Sources
/// содержит NexusSourceRef — ключ нужен.
/// </summary>
public sealed class PreflightNexusAuthStep
    : IStep<PreflightNexusAuthStep.Input, PreflightNexusAuthStep.Output>
{
    private readonly INexusApiKeyProvider _keyProvider;
    private readonly ILogger<PreflightNexusAuthStep> _logger;

    public PreflightNexusAuthStep(
        INexusApiKeyProvider keyProvider,
        ILogger<PreflightNexusAuthStep> logger)
    {
        _keyProvider = keyProvider;
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var needsNexus = ManifestNeedsNexus(input.Manifest);

        if (!needsNexus)
        {
            _logger.LogDebug(
                "Preflight Nexus auth: manifest has no nexus sources — skipping");
            return Task.FromResult(new Output { NeedsNexus = false });
        }

        var key = _keyProvider.TryGetApiKey();
        if (!string.IsNullOrWhiteSpace(key))
        {
            _logger.LogDebug(
                "Preflight Nexus auth: API key present");
            return Task.FromResult(new Output { NeedsNexus = true });
        }

        _logger.LogWarning(
            "Preflight Nexus auth: manifest has nexus sources " +
            "but API key is not set");

        throw new InvalidOperationException(
            "This pack requires Nexus downloads, but no Nexus API key is set. " +
            "Open Settings → Nexus, paste your API key, and retry. " +
            "If you're a Free user, enable Free Download in " +
            "Settings → Nexus Free Download.");
    }

    /// <summary>
    /// Проверяет, есть ли в манифесте хоть один nexus-источник.
    /// Смотрит manifest.Archives[] и manifest.Mo2.Archive.
    /// </summary>
    private static bool ManifestNeedsNexus(ModlistManifest manifest)
    {
        foreach (var archive in manifest.Archives)
        {
            foreach (var source in archive.Sources)
            {
                if (source is NexusSourceRef)
                    return true;
            }
        }

        foreach (var source in manifest.Mo2.Archive.Sources)
        {
            if (source is NexusSourceRef)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
    }

    public sealed class Output
    {
        /// <summary>
        /// true, если манифест требует Nexus-доступа.
        /// false — nexus-источников нет, ключ не нужен.
        /// </summary>
        public required bool NeedsNexus { get; init; }
    }
}
