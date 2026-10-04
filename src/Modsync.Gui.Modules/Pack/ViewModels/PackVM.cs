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
using Modsync.Pack.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

public sealed partial class PackVM : ProgressViewModel
{
    private readonly IPackRunner _runner;
    private readonly IServiceProvider _sp;
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

    [ObservableProperty]
    private CreatePackConfigVM? _createConfigVM;

    [ObservableProperty]
    private bool _isCreatingConfig;

    public FilePickerVM ConfigPicker { get; }
    public LogVM Log { get; }

    public PackVM(
        IPackRunner runner,
        IServiceProvider sp,
        IFilePickerService picker,
        LogVM log,
        ILogger<PackVM> logger)
    {
        _runner = runner;
        _sp = sp;
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

    public void SetNavigateHome(Action navigateHome) { }

    // ------------------------------------------------------------------
    //  Pack (обычный путь)
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task PackAsync()
    {
        var configPath = ConfigPicker.Path!;

        await ExecutePackAsync((progress, ct) =>
            _runner.RunAsync(configPath, progress, ct));

        await MaybeShowPatchDialogAsync();
    }

    private bool CanPack()
        => State == PackState.Configuration
           && !IsCreatingConfig
           && ConfigPicker.IsValid;

    // ------------------------------------------------------------------
    //  Create Pack Config
    // ------------------------------------------------------------------

    [RelayCommand]
    private void OpenCreateConfig()
    {
        if (IsCreatingConfig)
            return;

        var vm = _sp.GetRequiredService<CreatePackConfigVM>();
        vm.ConfigCreated += OnConfigCreated;
        vm.Cancelled += OnCreateConfigCancelled;

        CreateConfigVM = vm;
        IsCreatingConfig = true;
    }

    private void OnConfigCreated(PackConfigBuilderInput input)
    {
        CloseCreateConfig();
        _ = ExecuteAndMaybeShowPatchAsync(input);
    }

    private async Task ExecuteAndMaybeShowPatchAsync(PackConfigBuilderInput input)
    {
        await ExecutePackAsync((progress, ct) =>
            _runner.RunFromConfigBuilderAsync(input, progress, ct));

        await MaybeShowPatchDialogAsync();
    }

    private void OnCreateConfigCancelled()
    {
        CloseCreateConfig();
    }

    private void CloseCreateConfig()
    {
        if (CreateConfigVM is not null)
        {
            CreateConfigVM.ConfigCreated -= OnConfigCreated;
            CreateConfigVM.Cancelled -= OnCreateConfigCancelled;
        }

        CreateConfigVM = null;
        IsCreatingConfig = false;
    }

    // ------------------------------------------------------------------
    //  Unmatched dialog
    // ------------------------------------------------------------------

    private async Task MaybeShowPatchDialogAsync()
    {
        if (State != PackState.Success)
            return;

        if (Summary is null)
            return;

        var unmatched = Summary.UnmatchedFiles;
        if (unmatched <= 0)
            return;

        // instancePath берём из Summary — это правильный путь,
        // независимо от того, откуда пришёл config (файл или форма).
        var instancePath = Summary.InstancePath;

        try
        {
            var dialog = _sp.GetService<IPatchDialogService>();
            if (dialog is null)
            {
                _logger.LogDebug(
                    "IPatchDialogService not registered — skipping dialog");
                return;
            }

            await dialog.ShowAsync(instancePath, unmatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to show patch dialog; continuing");
        }
    }

    // ------------------------------------------------------------------
    //  Shared pack execution
    // ------------------------------------------------------------------

    private async Task ExecutePackAsync(
        Func<IProgress<StepProgress>, CancellationToken, Task<PackSummary>> run)
    {
        State = PackState.Packing;
        UpdateVisibility();

        _cts = new CancellationTokenSource();

        var progress = new Progress<StepProgress>(p =>
            Report(p.StepIndex, p.TotalSteps, p.StepName, p.Detail));

        try
        {
            Summary = await run(progress, _cts.Token);
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

    partial void OnIsCreatingConfigChanged(bool value)
    {
        UpdateVisibility();
        PackCommand.NotifyCanExecuteChanged();
    }

    private void UpdateVisibility()
    {
        IsConfiguring = State == PackState.Configuration && !IsCreatingConfig;
        IsPacking = State == PackState.Packing;
        IsSuccess = State == PackState.Success;
        IsFailure = State == PackState.Failure;
    }
}
