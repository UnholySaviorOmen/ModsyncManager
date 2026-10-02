// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Modsync.Install.Verify;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modsync.Install;

/// <summary>
/// DI-extension: регистрирует все сервисы installer-а.
///
/// Хеш-кеши:
///   - FileHashCache — in-memory, для ArchiveMatcher (не в этом DI).
///   - SqliteHashCache — L1+L2, для шагов с persist.
///   - IHashCache — алиас на SqliteHashCache.
///
/// Скачивание:
///   - MirrorDownloader и NexusDownloader регистрируются через
///     TryAddEnumerable(ServiceDescriptor.Singleton&lt;IArchiveDownloader, TConcrete&gt;()).
/// </summary>
public static class InstallServices
{
    public static IServiceCollection AddModsyncInstall(this IServiceCollection services)
    {
        // --- Core-сервисы, общие с packer ---
        services.TryAddSingleton<FileHashCache>();
        services.TryAddSingleton<IHashCache, SqliteHashCache>();
        services.TryAddSingleton<IArchiveExtractor, SevenZipExtractor>();

        // --- Nexus ---
        services.TryAddSingleton<INexusApiKeyProvider, NexusApiKeyProvider>();
        services.TryAddSingleton<INexusCredentialValidator, NexusCredentialValidator>();
        services.TryAddSingleton<INexusFreeNxmProvider, NullNexusFreeNxmProvider>();

        // --- HttpClient-ы (именованные) ---
        services.AddHttpClient(MirrorDownloader.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddHttpClient(NexusDownloader.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddHttpClient("nexus-api", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        // --- NexusClient (API) ---
        services.TryAddSingleton<NexusClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new NexusClient(
                factory.CreateClient("nexus-api"),
                sp.GetRequiredService<INexusApiKeyProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NexusClient>>());
        });

        // --- Downloaders в коллекции IArchiveDownloader ---
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IArchiveDownloader, MirrorDownloader>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IArchiveDownloader, NexusDownloader>());

        services.TryAddSingleton<DownloaderRegistry>();

        // --- Install steps ---
        services.TryAddSingleton<ReadManifestStep>();
        services.TryAddSingleton<ResolveTargetStep>();
        services.TryAddSingleton<ValidateTargetStep>();
        services.TryAddSingleton<BootstrapInstanceStep>();
        services.TryAddSingleton<BootstrapMo2Step>();
        services.TryAddSingleton<SyncArchivesStep>();
        services.TryAddSingleton<ExecuteExtensionsStep>();
        services.TryAddSingleton<ExecuteExtrasStep>();
        services.TryAddSingleton<SyncModsStep>();
        services.TryAddSingleton<GenerateMetaIniStep>();
        services.TryAddSingleton<RegenerateProfileStep>();

        // --- Verify ---
        services.TryAddSingleton<VerifyPipeline>();

        // --- Pipeline ---
        services.TryAddSingleton<InstallPipeline>();

        return services;
    }
}
