// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM экрана Home — дашборд готовых к установке сборок.
///
/// Сканирует &lt;exeDir&gt;/Instances/ через IInstalledPackScanner,
/// строит карточки InstalledPackVM. Пустой список — пустой экран.
///
/// Реализует IInstallRequestHandler: при клике на Install/Update
/// в карточке поднимает InstallRequested(manifestPath, targetPath).
/// MainWindowVM переключает на Install и передаёт пути.
///
/// Refresh() вызывается MainWindowVM при каждой навигации на Home,
/// чтобы список отражал актуальное состояние Instances/.
/// </summary>
public sealed partial class HomeVM : ViewModel, IInstallRequestHandler
{
    private readonly IInstalledPackScanner _scanner;
    private readonly IFilePickerService _picker;
    private readonly IProcessLauncher _launcher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HomeVM> _logger;

    public event Action<string, string>? InstallRequested;

    public ObservableCollection<InstalledPackVM> Items { get; } = new();

    public HomeVM(
        IInstalledPackScanner scanner,
        IFilePickerService picker,
        IProcessLauncher launcher,
        ILoggerFactory loggerFactory,
        ILogger<HomeVM> logger)
    {
        _scanner = scanner;
        _picker = picker;
        _launcher = launcher;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public void Refresh()
    {
        Items.Clear();

        var packs = _scanner.Scan();

        foreach (var pack in packs)
        {
            Items.Add(new InstalledPackVM(
                pack,
                OnInstallRequested,
                _picker,
                _launcher,
                _loggerFactory.CreateLogger<InstalledPackVM>()));
        }

        _logger.LogDebug(
            "Home refreshed: {Count} pack(s)", Items.Count);
    }

    private void OnInstallRequested(string manifestPath, string targetPath)
    {
        InstallRequested?.Invoke(manifestPath, targetPath);
    }
}
