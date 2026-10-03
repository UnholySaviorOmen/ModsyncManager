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

    // ------------------------------------------------------------------
    //  Create Pack Config (embedded, not overlay)
    // ------------------------------------------------------------------

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

    public void SetNavigateHome(Action navigateHome)
    {
        // Оставлено для совместимости с INavigationAware.
        // В UI кнопка Home заменена на Done (3.9.6).
    }

    // ------------------------------------------------------------------
    //  Pack (обычный путь, с ConfigPicker)
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task PackAsync()
    {
        await ExecutePackAsync((progress, ct) =>
            _runner.RunAsync(ConfigPicker.Path!, progress, ct));
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
        // Закрыть форму синхронно.
        CloseCreateConfig();

        // Запустить pack асинхронно. ExecutePackAsync сам ловит
        // все исключения.
        _ = ExecutePackAsync((progress, ct) =>
            _runner.RunFromConfigBuilderAsync(input, progress, ct));
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

    // ------------------------------------------------------------------
    //  Cancel
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCancel() => State == PackState.Packing;

    // ------------------------------------------------------------------
    //  Done
    // ------------------------------------------------------------------

    [RelayCommand]
    private void Done()
    {
        Summary = null;
        ErrorMessage = null;
        State = PackState.Configuration;
    }

    // ------------------------------------------------------------------
    //  State machine
    // ------------------------------------------------------------------

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
