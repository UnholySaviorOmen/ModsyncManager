// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Modules.Install.ViewModels;
using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Gui.Modules.Verify.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ModsyncManager.Gui.Services;

internal sealed class ScreenFactory : IScreenFactory
{
    private readonly IServiceProvider _sp;

    public ScreenFactory(IServiceProvider sp) => _sp = sp;

    public object Create(ScreenType screen) => screen switch
    {
        ScreenType.Home => _sp.GetRequiredService<HomeVM>(),
        ScreenType.Install => _sp.GetRequiredService<InstallVM>(),
        ScreenType.Pack => _sp.GetRequiredService<PackVM>(),
        ScreenType.Verify => _sp.GetRequiredService<VerifyVM>(),
        ScreenType.Logs => _sp.GetRequiredService<LogsVM>(),
        ScreenType.Cache => _sp.GetRequiredService<CacheVM>(),
        ScreenType.Settings => _sp.GetRequiredService<SettingsVM>(),
        _ => _sp.GetRequiredService<HomeVM>(),
    };
}
