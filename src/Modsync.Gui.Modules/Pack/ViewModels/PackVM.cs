// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CodingSeb.Localization;
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
    private readonly IFilePickerService _picker;
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
    private PackConfigVM? _configVM;

    [ObservableProperty]
    private bool _isCreatingConfig;

    public FilePickerVM LoadConfigPicker { get; }
    public LogVM Log { get; }

    // ------------------------------------------------------------------
    //  Computed: summary
    // ------------------------------------------------------------------

    public string ModsScannedText => Summary is null
        ? ""
        : string.Format(
            Loc.Tr("Pack.Summary_ModsScanned_Value"),
            Summary.ModsScanned,
            Summary.FilesScanned);

    public string DirectivesText => Summary is null
        ? ""
        : string.Format(
            Loc.Tr("Pack.Summary_Directives_Value"),
            Summary.DirectivesTotal,
            Summary.DirectivesFromArchive);

    public string ArchivesText => Summary is null
        ? ""
        : string.Format(
            Loc.Tr("Pack.Summary_Archives_Value"),
            Summary.ArchivesResolved,
            Summary.ArchivesUnresolved);

    public string ManifestText => Summary is null
        ? ""
        : string.Format(
            Loc.Tr("Pack.Summary_Manifest_Value"),
            Summary.ManifestMods,
            Summary.ManifestArchives,
            Summary.ManifestExtensions,
            Summary.ManifestExtras);

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

        LoadConfigPicker = new FilePickerVM(picker)
        {
            Placeholder = Loc.Tr("Pack.LoadConfigPicker_Placeholder"),
            FilterHint = ".json",
            MustExist = true,
            Folder = false,
        };
        LoadConfigPicker.Picked += OnLoadConfigPicked;
    }

    public void SetNavigateHome(Action navigateHome) { }

    // ------------------------------------------------------------------
    //  React on picker
    // ------------------------------------------------------------------

    private void OnLoadConfigPicked(object? sender, string path)
    {
        _ = TryOpenConfigAsync(path);
    }

    private async Task TryOpenConfigAsync(string configPath)
    {
        await OpenConfigAsync(configPath);

        // Сбрасываем picker, чтобы при следующем открытии
        // он был пустым.
        LoadConfigPicker.Clear();
    }

    /// <summary>
    /// Открыть форму PackConfig с config-ом из указанного пути.
    /// Идемпотентно: если форма уже открыта (IsCreatingConfig) —
    /// no-op. Используется picker-ом и тестами.
    /// </summary>
    public async Task OpenConfigAsync(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return;

        if (IsCreatingConfig)
            return;

        try
        {
            var vm = _sp.GetRequiredService<PackConfigVM>();
            vm.ConfigCreated += OnConfigCreated;
            vm.Cancelled += OnConfigCancelled;

            ConfigVM = vm;
            IsCreatingConfig = true;

            await vm.LoadConfigAsync(configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to open config: {Path}", configPath);
            ErrorMessage = ex.Message;
        }
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
            ErrorMessage = Loc.Tr("Common.Cancelled");
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

    partial void OnSummaryChanged(PackSummary? value)
    {
        OnPropertyChanged(nameof(ModsScannedText));
        OnPropertyChanged(nameof(DirectivesText));
        OnPropertyChanged(nameof(ArchivesText));
        OnPropertyChanged(nameof(ManifestText));
    }

    private void UpdateVisibility()
    {
        IsConfiguring = State == PackState.Configuration && !IsCreatingConfig;
        IsPacking = State == PackState.Packing;
        IsSuccess = State == PackState.Success;
        IsFailure = State == PackState.Failure;
    }
}
