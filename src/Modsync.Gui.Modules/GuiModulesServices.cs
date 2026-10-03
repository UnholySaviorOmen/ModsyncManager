// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Modules.Install.Services;
using Modsync.Gui.Modules.Install.ViewModels;
using Modsync.Gui.Modules.Pack.Services;
using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Verify.Services;
using Modsync.Gui.Modules.Verify.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Modsync.Gui.Modules;

public static class GuiModulesServices
{
    public static IServiceCollection AddGuiModules(this IServiceCollection services)
    {
        services.AddSingleton<IInstallRunner, InstallRunner>();
        services.AddSingleton<InstallVM>();

        services.AddSingleton<IPackRunner, PackRunner>();
        services.AddSingleton<PackVM>();

        // Create Pack Config — transient: каждый раз новая форма.
        services.AddTransient<CreatePackConfigVM>();

        services.AddSingleton<IVerifyRunner, VerifyRunner>();
        services.AddSingleton<VerifyVM>();

        return services;
    }
}
