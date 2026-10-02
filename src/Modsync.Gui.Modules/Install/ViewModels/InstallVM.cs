// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core;
using Modsync.Core.Progress;
using Modsync.Gui.Modules.Install.Services;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Gui.Shared.ViewModels.Controls;
using Modsync.Install;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Install.ViewModels;

public sealed partial class InstallVM : ProgressViewModel, IInstallTarget
{
    private readonly IInstallRunner _runner;
    private readonly ILogger<InstallVM> _logger;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private InstallState _state = InstallState.Configuration;

    [ObservableProperty]
    private bool _isConfiguring = true;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isFailure;

    [ObservableProperty]
    private InstallSummary? _summary;

    [ObservableProperty]
    private string? _errorMessage;

    public FilePickerVM ModlistPicker { get; }
    public FilePickerVM TargetPicker { get; }
    public LogVM Log { get; }

    public InstallVM(
        IInstallRunner runner,
        IFilePickerService picker,
        LogVM log,
        ILogger<InstallVM> logger)
    {
        _runner = runner;
        _logger = logger;
        Log = log;

        ModlistPicker = new FilePickerVM(picker)
        {
            Placeholder = "Select modlist.json",
            FilterHint = ".json",
            MustExist = true,
            Folder = false,
        };
        ModlistPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilePickerVM.IsValid))
                InstallCommand.NotifyCanExecuteChanged();
        };

        TargetPicker = new FilePickerVM(picker)
        {
            Placeholder = "Target folder (optional)",
            MustExist = false,
            Folder = true,
        };
    }

    public void SetNavigateHome(Action navigateHome)
    {
        // Оставлено для совместимости с INavigationAware.
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        State = InstallState.Installing;
        UpdateVisibility();

        _cts = new CancellationTokenSource();

        var progress = new Progress<StepProgress>(p =>
            Report(p.StepIndex, p.TotalSteps, p.StepName, p.Detail));

        try
        {
            var target = string.IsNullOrWhiteSpace(TargetPicker.Path)
                ? null
                : TargetPicker.Path;

            Summary = await _runner.RunAsync(
                ModlistPicker.Path!, target, progress, _cts.Token);

            State = InstallState.Success;
        }
        catch (Exception ex) when (CancellationHelper.IsCancellation(ex))
        {
            _logger.LogInformation("Install cancelled by user");
            ErrorMessage = "Cancelled.";
            State = InstallState.Configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install failed");
            ErrorMessage = ex.Message;
            State = InstallState.Failure;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            UpdateVisibility();
        }
    }

    private bool CanInstall()
        => State == InstallState.Configuration && ModlistPicker.IsValid;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCancel() => State == InstallState.Installing;

    [RelayCommand]
    private void Done()
    {
        Summary = null;
        ErrorMessage = null;
        Reset();
        State = InstallState.Configuration;
    }

    partial void OnStateChanged(InstallState value)
    {
        UpdateVisibility();
        InstallCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void UpdateVisibility()
    {
        IsConfiguring = State == InstallState.Configuration;
        IsInstalling = State == InstallState.Installing;
        IsSuccess = State == InstallState.Success;
        IsFailure = State == InstallState.Failure;
    }

    /// <summary>
    /// Реализация IInstallTarget. Вызывается MainWindowVM при
    /// навигации из Home-дашборда.
    /// </summary>
    public void PrepareForInstall(string manifestPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            _logger.LogWarning(
                "PrepareForInstall called with empty manifest path");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            _logger.LogWarning(
                "PrepareForInstall called with empty target path");
            return;
        }

        ModlistPicker.SetPath(manifestPath);
        TargetPicker.SetPath(targetPath);

        if (State != InstallState.Configuration)
        {
            Summary = null;
            ErrorMessage = null;
            Reset();
            State = InstallState.Configuration;
        }
    }
}
