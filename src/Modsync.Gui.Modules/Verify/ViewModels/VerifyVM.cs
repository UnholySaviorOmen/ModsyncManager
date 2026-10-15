// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Gui.Shared.ViewModels.Controls;
using Modsync.Gui.Modules.Verify.Services;
using Modsync.Install.Verify;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Verify.ViewModels;

public sealed partial class VerifyVM : ViewModel
{
    private readonly IVerifyRunner _runner;
    private readonly ILogger<VerifyVM> _logger;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private VerifyState _state = VerifyState.Configuration;

    [ObservableProperty]
    private bool _isConfiguring = true;

    [ObservableProperty]
    private bool _isVerifying;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isFailure;

    [ObservableProperty]
    private VerifyReport? _report;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _showAllChecks;

    public FilePickerVM TargetPicker { get; }
    public LogVM Log { get; }

    public ObservableCollection<VerifyRowVM> Rows { get; } = new();

    // ------------------------------------------------------------------
    //  Производные свойства для UI
    // ------------------------------------------------------------------

    public bool IsOk => Report?.IsOk ?? false;

    public bool HasFailures => Report is not null && Report.FailedCount > 0;

    public int PassedCount => Report?.PassedCount ?? 0;
    public int FailedCount => Report?.FailedCount ?? 0;

    public string ResultTitle => Report switch
    {
        null => "",
        { IsOk: true } => Loc.Tr("Verify.Result_AllPassed"),
        _ => string.Format(
            Loc.Tr("Verify.Result_FailedFormat"),
            Report.FailedCount),
    };

    public string ResultColor => IsOk ? "#7fc98a" : "#d97777";

    public string TargetPath => Report?.TargetPath ?? "";

    public VerifyVM(
        IVerifyRunner runner,
        IFilePickerService picker,
        LogVM log,
        ILogger<VerifyVM> logger)
    {
        _runner = runner;
        _logger = logger;
        Log = log;

        TargetPicker = new FilePickerVM(picker)
        {
            Placeholder = Loc.Tr("Verify.TargetPicker_Placeholder"),
            MustExist = true,
            Folder = true,
        };
        TargetPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilePickerVM.IsValid))
                VerifyCommand.NotifyCanExecuteChanged();
        };
    }

    public void SetNavigateHome(Action navigateHome)
    {
        // Оставлено для совместимости с INavigationAware.
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        Rows.Clear();

        State = VerifyState.Verifying;
        UpdateVisibility();

        _cts = new CancellationTokenSource();

        try
        {
            Report = await _runner.RunAsync(TargetPicker.Path!, _cts.Token);

            RebuildRows();
            NotifyResultChanged();

            State = VerifyState.Success;
        }
        catch (Exception ex) when (CancellationHelper.IsCancellation(ex))
        {
            _logger.LogInformation("Verify cancelled by user");
            ErrorMessage = Loc.Tr("Common.Cancelled");
            State = VerifyState.Configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verify failed");
            ErrorMessage = ex.Message;
            State = VerifyState.Failure;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            UpdateVisibility();
        }
    }

    private bool CanVerify()
        => State == VerifyState.Configuration && TargetPicker.IsValid;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCancel() => State == VerifyState.Verifying;

    [RelayCommand]
    private void Done()
    {
        Report = null;
        ErrorMessage = null;
        Rows.Clear();
        NotifyResultChanged();
        State = VerifyState.Configuration;
    }

    partial void OnStateChanged(VerifyState value)
    {
        UpdateVisibility();
        VerifyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowAllChecksChanged(bool value)
    {
        if (Report is not null)
            RebuildRows();
    }

    private void UpdateVisibility()
    {
        IsConfiguring = State == VerifyState.Configuration;
        IsVerifying = State == VerifyState.Verifying;
        IsSuccess = State == VerifyState.Success;
        IsFailure = State == VerifyState.Failure;
    }

    // ------------------------------------------------------------------
    //  Построение строк
    // ------------------------------------------------------------------

    private void RebuildRows()
    {
        Rows.Clear();

        if (Report is null) return;

        var source = ShowAllChecks ? Report.Checks : Report.Failures;

        foreach (var check in source)
        {
            Rows.Add(new VerifyRowVM
            {
                Name = check.Name,
                Passed = check.Passed,
                Message = check.Message,
            });
        }
    }

    private void NotifyResultChanged()
    {
        OnPropertyChanged(nameof(IsOk));
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(PassedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultColor));
        OnPropertyChanged(nameof(TargetPath));
    }
}
