// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM экрана Home — дашборд готовых к установке сборок.
///
/// Сканирует &lt;exeDir&gt;/Instances/ через IInstalledPackScanner,
/// строит карточки InstalledPackVM. Пустой список — пустое
/// состояние с призывом установить сборку.
///
/// Реализует IInstallRequestHandler: при клике на Install/Update
/// в карточке поднимает InstallRequested(manifestPath, targetPath).
/// MainWindowVM переключает на Install и передаёт пути.
///
/// Кнопка «Установить сборку…» на пустом экране поднимает то же
/// событие с пустыми строками — сигнал «просто открыть Install».
/// MainWindowVM обрабатывает пустые пути как «без предзаполнения».
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

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;

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

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));

        _logger.LogDebug(
            "Home refreshed: {Count} pack(s)", Items.Count);
    }

    [RelayCommand]
    private void OpenInstall()
    {
        // Пустые строки — сигнал «открыть Install без предзаполнения».
        // MainWindowVM.OnInstallRequested проверяет и пропускает
        // PrepareForInstall.
        InstallRequested?.Invoke("", "");
    }

    private void OnInstallRequested(string manifestPath, string targetPath)
    {
        InstallRequested?.Invoke(manifestPath, targetPath);
    }
}
