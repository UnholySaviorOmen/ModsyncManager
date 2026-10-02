// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using Modsync.Gui.Shared.Navigation;

namespace Modsync.Gui.Shared.ViewModels;

public sealed partial class MainWindowVM : ViewModel
{
    private readonly IScreenFactory _screens;

    [ObservableProperty]
    private object? _activePane;

    public NavigationVM Navigation { get; }
    public HomeVM Home { get; }

    public MainWindowVM(IScreenFactory screens, SettingsVM settings)
    {
        _screens = screens;

        Home = (HomeVM)_screens.Create(ScreenType.Home);
        Navigation = new NavigationVM(NavigateTo, settings);

        // NavigateTo выполняет всю инициализацию: ActivePane, SelectScreen,
        // подписку на Home.InstallRequested, Refresh. Вызывать Home.Refresh()
        // напрямую нельзя — иначе подписка на InstallRequested не произойдёт,
        // и кнопка Install на карточке будет "мёртвой" до первой навигации.
        NavigateTo(ScreenType.Home);
    }

    public void NavigateTo(ScreenType screen)
    {
        var pane = _screens.Create(screen);

        ActivePane = pane;
        Navigation.SelectScreen(screen);

        if (pane is HomeVM home)
        {
            home.InstallRequested -= OnInstallRequested;
            home.InstallRequested += OnInstallRequested;
            home.Refresh();
        }
    }

    private void OnInstallRequested(string manifestPath, string targetPath)
    {
        NavigateTo(ScreenType.Install);

        if (ActivePane is IInstallTarget target)
            target.PrepareForInstall(manifestPath, targetPath);
    }
}
