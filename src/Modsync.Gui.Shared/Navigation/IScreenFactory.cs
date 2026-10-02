// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Navigation;

/// <summary>
/// Фабрика VM-инстансов по ScreenType.
///
/// Реализуется в клиентском проекте (ModsyncManager.Gui), потому что только он
/// знает про все VM (HomeVM из Shared, InstallVM из Gui.Modules и т.д.).
///
/// MainWindowVM резолвит панели через эту фабрику и не знает о конкретных
/// типах InstallVM/PackVM/VerifyVM.
/// </summary>
public interface IScreenFactory
{
    object Create(ScreenType screen);
}
