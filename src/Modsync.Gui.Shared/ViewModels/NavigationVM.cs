// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Modsync.Gui.Shared.Navigation;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM сайдбара.
///
/// Список пунктов зависит от SettingsVM.IsDevMode:
///   - DevMode = false: Home, Settings.
///   - DevMode = true:  Home, Install, Pack, Verify, Logs, Cache, Settings.
///
/// Подписан на SettingsVM.PropertyChanged. При смене IsDevMode
/// перестраивает Items. Если текущий SelectedItem скрывается —
/// переключается на Home и уведомляет _navigate.
///
/// _suppressCallback защищает от рекурсии: SelectScreen и RebuildItems
/// меняют SelectedItem программно, не желая дёргать callback.
/// </summary>
public sealed partial class NavigationVM : ViewModel
{
    private readonly Action<ScreenType> _navigate;
    private readonly SettingsVM _settings;
    private bool _suppressCallback;

    public ObservableCollection<NavigationItem> Items { get; }

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    public NavigationVM(Action<ScreenType> navigate, SettingsVM settings)
    {
        _navigate = navigate;
        _settings = settings;

        Items = new ObservableCollection<NavigationItem>();
        RebuildItemsCore();

        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public void SelectScreen(ScreenType screen)
    {
        var item = Items.FirstOrDefault(i => i.Screen == screen);
        if (item is null) return;

        _suppressCallback = true;
        SelectedItem = item;
        _suppressCallback = false;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsVM.IsDevMode))
            return;

        RebuildItems();
    }

    private void RebuildItems()
    {
        var previousScreen = SelectedItem?.Screen;

        RebuildItemsCore();

        if (previousScreen is ScreenType prev
            && Items.Any(i => i.Screen == prev))
        {
            SelectScreen(prev);
            return;
        }

        SelectScreen(ScreenType.Home);
        _navigate(ScreenType.Home);
    }

    private void RebuildItemsCore()
    {
        Items.Clear();

        Items.Add(new NavigationItem("Dashboard", ScreenType.Home));

        if (_settings.IsDevMode)
        {
            Items.Add(new NavigationItem("Install", ScreenType.Install));
            Items.Add(new NavigationItem("Pack", ScreenType.Pack));
            Items.Add(new NavigationItem("Verify", ScreenType.Verify));
            Items.Add(new NavigationItem("Logs", ScreenType.Logs));
            Items.Add(new NavigationItem("Cache", ScreenType.Cache));
        }

        Items.Add(new NavigationItem("Settings", ScreenType.Settings));
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (_suppressCallback) return;
        if (value is null) return;

        _navigate(value.Screen);
    }
}
