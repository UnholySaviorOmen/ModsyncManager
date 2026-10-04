// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Modsync.Gui.Modules;
using ModsyncManager.Gui.Services;
using Modsync.Gui.Shared;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Install;
using Modsync.Pack;
using Modsync.Logging;
using Modsync.Platform.Nexus;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModsyncManager.Gui;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        DataTemplates.Add(new ViewLocator());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services = BuildServices();

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowVM>(),
            };

            StartNxmReceiver();

            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ------------------------------------------------------------------
    //  Nxm URL receiver lifecycle
    // ------------------------------------------------------------------

    private static void StartNxmReceiver()
    {
        try
        {
            var receiver = Services.GetRequiredService<INxmUrlReceiver>();
            var logger = Services.GetRequiredService<ILogger<App>>();

            _ = receiver.StartAsync(CancellationToken.None)
                .ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                        {
                            logger.LogWarning(
                                t.Exception,
                                "Failed to start nxm URL receiver. " +
                                "Free download will not work.");
                        }
                    },
                    TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Failed to resolve INxmUrlReceiver: {ex.Message}");
        }
    }

    private static async void OnShutdownRequested(
        object? sender, ShutdownRequestedEventArgs e)
    {
        e.Cancel = false;

        try
        {
            var receiver = Services.GetService<INxmUrlReceiver>();
            if (receiver is null)
                return;

            var stopTask = receiver.StopAsync();
            await Task.WhenAny(
                stopTask,
                Task.Delay(TimeSpan.FromSeconds(2)));
        }
        catch
        {
            // Shutdown не должен падать из-за ошибок cleanup.
        }
    }

    // ------------------------------------------------------------------
    //  DI
    // ------------------------------------------------------------------

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.TimestampFormat = "HH:mm:ss ";
                opts.ColorBehavior =
                    Microsoft.Extensions.Logging.Console
                        .LoggerColorBehavior.Disabled;
            });

            builder.AddFileLogger();

            builder.SetMinimumLevel(LogLevel.Information);

            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
            builder.AddFilter("Polly", LogLevel.Warning);
        });

        services.AddModsyncInstall();
        services.AddModsyncPack();
        services.AddModsyncNxm();

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IProcessLauncher, ShellProcessLauncher>();
        services.AddSingleton<IScreenFactory, ScreenFactory>();
        services.AddSingleton<IPatchDialogService, AvaloniaPatchDialogService>();

        services.AddSingleton<ISettingsStore, SettingsStore>();

        services.AddGuiShared();
        services.AddGuiModules();

        services.AddSingleton<MainWindowVM>();

        return services.BuildServiceProvider();
    }
}
