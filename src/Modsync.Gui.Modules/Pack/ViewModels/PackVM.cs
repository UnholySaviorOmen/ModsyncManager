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
using Modsync.Pack;
using Modsync.Pack.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// VM экрана Pack.
///
/// Один путь: пользователь нажимает «Load config…» (выбирает
/// существующий modsyncmanager-pack.json) или «Create config…»
/// (открывает пустую форму). Обе кнопки открывают embedded-форму
/// PackConfigVM.
///
/// Форма либо загружает config из файла, либо пустая. При нажатии
/// Pack внутри формы сохраняет config на диск и поднимает
/// ConfigCreated(input). PackVM ловит событие, запускает pipeline
/// через IPackRunner.RunFromConfigBuilderAsync.
///
/// После успешного pack, если PackSummary.UnmatchedFiles > 0,
/// показывается модальный диалог через IPatchDialogService.
/// </summary>
public sealed partial class PackVM : ProgressViewModel
{
    private readonly IPackRunner _runner;
    private readonly IServiceProvider _sp;
    private readonly IFilePickerService _picker;
    private readonly ILogger<PackVM> _logger;
    private CancellationTokenSource? _cts;

    // ------------------------------------------------------------------
    //  ObservableProperty-поля
    // ------------------------------------------------------------------

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
    //  Config form (embedded)
    //  Имя свойства — ConfigVM (тип PackConfigVM).
    //  Имя класса и свойства не должны совпадать (CS0542), поэтому
    //  свойство называется ConfigVM, а не PackConfigVM.
    // ------------------------------------------------------------------

    [ObservableProperty]
    private PackConfigVM? _configVM;

    [ObservableProperty]
    private bool _isCreatingConfig;

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
        _picker = picker;
        _logger = logger;
        Log = log;
    }

    public void SetNavigateHome(Action navigateHome) { }

    // ------------------------------------------------------------------
    //  Load config… (из файла)
    // ------------------------------------------------------------------

    /// <summary>
    /// Открывает file picker на modsyncmanager-pack.json, резолвит
    /// путь, создаёт PackConfigVM и вызывает LoadConfigAsync(path).
    /// </summary>
    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        if (IsCreatingConfig)
            return;

        var picked = await _picker.PickFileAsync(
            "Select modsyncmanager-pack.json", ".json");

        if (string.IsNullOrWhiteSpace(picked))
            return;

        var vm = _sp.GetRequiredService<PackConfigVM>();
        vm.ConfigCreated += OnConfigCreated;
        vm.Cancelled += OnConfigCancelled;

        ConfigVM = vm;
        IsCreatingConfig = true;

        // Загружаем config в форму. Ошибки парсинга — ErrorMessage
        // внутри PackConfigVM, форма остаётся открытой.
        await vm.LoadConfigAsync(picked);
    }

    // ------------------------------------------------------------------
    //  Create config… (пустая форма)
    // ------------------------------------------------------------------

    [RelayCommand]
    private void CreateConfig()
    {
        if (IsCreatingConfig)
            return;

        var vm = _sp.GetRequiredService<PackConfigVM>();
        vm.ConfigCreated += OnConfigCreated;
        vm.Cancelled += OnConfigCancelled;

        ConfigVM = vm;
        IsCreatingConfig = true;
    }

    // ------------------------------------------------------------------
    //  Config form — завершение
    // ------------------------------------------------------------------

    private void OnConfigCreated(PackConfigBuilderInput input)
    {
        CloseConfig();
        _ = ExecuteAndMaybeShowPatchAsync(input);
    }

    private async Task ExecuteAndMaybeShowPatchAsync(PackConfigBuilderInput input)
    {
        await ExecutePackAsync((progress, ct) =>
            _runner.RunFromConfigBuilderAsync(input, progress, ct));

        await MaybeShowPatchDialogAsync();
    }

    private void OnConfigCancelled()
    {
        CloseConfig();
    }

    private void CloseConfig()
    {
        if (ConfigVM is not null)
        {
            ConfigVM.ConfigCreated -= OnConfigCreated;
            ConfigVM.Cancelled -= OnConfigCancelled;
        }

        ConfigVM = null;
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
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCreatingConfigChanged(bool value)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        IsConfiguring = State == PackState.Configuration && !IsCreatingConfig;
        IsPacking = State == PackState.Packing;
        IsSuccess = State == PackState.Success;
        IsFailure = State == PackState.Failure;
    }
}
