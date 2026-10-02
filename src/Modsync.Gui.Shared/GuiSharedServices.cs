// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared;

public static class GuiSharedServices
{
    public static IServiceCollection AddGuiShared(this IServiceCollection services)
    {
        services.AddSingleton<ObservableLogSink>();

        services.AddSingleton<ILoggerProvider>(sp =>
            new ObservableLoggerProvider(
                sp.GetRequiredService<ObservableLogSink>(),
                minLevel: LogLevel.Information));

        services.AddSingleton<LogVM>();
        services.AddSingleton<HomeVM>();
        services.AddSingleton<LogsVM>();

        services.AddSingleton<NexusSettingsVM>();
        services.AddSingleton<NexusFreeDownloadSettingsVM>();
        services.AddSingleton<SettingsVM>();

        // CacheVM — DevMode-экран с техническими базами.
        services.AddSingleton<CacheVM>();

        services.AddSingleton<IInstalledPackScanner>(sp =>
            new InstalledPackScanner(
                Path.Combine(AppContext.BaseDirectory, "Instances"),
                sp.GetRequiredService<ILogger<InstalledPackScanner>>()));

        return services;
    }
}
