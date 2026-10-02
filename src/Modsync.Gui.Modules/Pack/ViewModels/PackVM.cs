// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core;
using Modsync.Core.Progress;
using Modsync.Gui.Modules.Pack.Services;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Gui.Shared.ViewModels.Controls;
using Modsync.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

public sealed partial class PackVM : ProgressViewModel
{
    private readonly IPackRunner _runner;
    private readonly ILogger<PackVM> _logger;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private PackState _state = PackState.Configuration;

    [ObservableProperty]
    private bool _isConfiguring = true;

    [ObservableProperty]
    private bool _isPacking;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isFailure;

    [ObservableProperty]
    private PackSummary? _summary;

    [ObservableProperty]
    private string? _errorMessage;

    public FilePickerVM ConfigPicker { get; }
    public LogVM Log { get; }

    public PackVM(
        IPackRunner runner,
        IFilePickerService picker,
        LogVM log,
        ILogger<PackVM> logger)
    {
        _runner = runner;
        _logger = logger;
        Log = log;

        ConfigPicker = new FilePickerVM(picker)
        {
            Placeholder = "Select modsyncmanager-pack.json",
            FilterHint = ".json",
            MustExist = true,
            Folder = false,
        };
        ConfigPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilePickerVM.IsValid))
                PackCommand.NotifyCanExecuteChanged();
        };
    }

    public void SetNavigateHome(Action navigateHome)
    {
        // Оставлено для совместимости с INavigationAware.
        // В UI кнопка Home заменена на Done (3.9.6).
    }

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task PackAsync()
    {
        State = PackState.Packing;
        UpdateVisibility();

        _cts = new CancellationTokenSource();

        var progress = new Progress<StepProgress>(p =>
            Report(p.StepIndex, p.TotalSteps, p.StepName));

        try
        {
            Summary = await _runner.RunAsync(
                ConfigPicker.Path!, progress, _cts.Token);

            State = PackState.Success;
        }
        catch (Exception ex) when (CancellationHelper.IsCancellation(ex))
        {
            _logger.LogInformation("Pack cancelled by user");
            ErrorMessage = "Cancelled.";
            State = PackState.Configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pack failed");
            ErrorMessage = ex.Message;
            State = PackState.Failure;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            UpdateVisibility();
        }
    }

    private bool CanPack()
        => State == PackState.Configuration && ConfigPicker.IsValid;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCancel() => State == PackState.Packing;

    [RelayCommand]
    private void Done()
    {
        Summary = null;
        ErrorMessage = null;
        State = PackState.Configuration;
    }

    partial void OnStateChanged(PackState value)
    {
        UpdateVisibility();
        PackCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void UpdateVisibility()
    {
        IsConfiguring = State == PackState.Configuration;
        IsPacking = State == PackState.Packing;
        IsSuccess = State == PackState.Success;
        IsFailure = State == PackState.Failure;
    }
}
