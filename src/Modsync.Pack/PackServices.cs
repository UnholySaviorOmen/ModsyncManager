// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Pack.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modsync.Pack;

/// <summary>
/// DI-extension: регистрирует все сервисы packer-а.
///
/// Хеш-кеши:
///   - FileHashCache — in-memory, для ArchiveMatcher (temp-папки,
///     persist не имеет смысла).
///   - SqliteHashCache — L1+L2, для шагов с persist (ScanModsStep,
///     ScanExtensionsStep, ScanExtrasStep, IndexArchivesStep).
///   - IHashCache — алиас на SqliteHashCache для шагов через DI.
/// </summary>
public static class PackServices
{
    public static IServiceCollection AddModsyncPack(this IServiceCollection services)
    {
        // --- Core-сервисы, общие с installer ---
        services.TryAddSingleton<FileHashCache>();
        services.TryAddSingleton<IHashCache, SqliteHashCache>();
        services.TryAddSingleton<IArchiveExtractor, SevenZipExtractor>();

        // --- Pack steps ---
        services.TryAddSingleton<ReadConfigStep>();
        services.TryAddSingleton<ReadInstanceStep>();
        services.TryAddSingleton<IndexArchivesStep>();
        services.TryAddSingleton<ScanModsStep>();
        services.TryAddSingleton<ScanExtensionsStep>();
        services.TryAddSingleton<ScanExtrasStep>();
        services.TryAddSingleton<MatchStep>();
        services.TryAddSingleton<MatchExtensionsStep>();
        services.TryAddSingleton<MatchExtrasStep>();
        services.TryAddSingleton<BuildManifestStep>();
        services.TryAddSingleton<ValidateManifestStep>();
        services.TryAddSingleton<WriteManifestStep>();

        services.TryAddSingleton<PackPipeline>();

        // --- PackConfigBuilder (для GUI «Create Pack Config») ---
        services.TryAddSingleton<PackConfigBuilder>();

        return services;
    }
}
