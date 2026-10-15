// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

public sealed partial class PatchArchiveDialogVM : ViewModel
{
    private const string OutputDirName = "__ModsyncManager_Output";

    private readonly PatchArchiveBuilder _builder;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<PatchArchiveDialogVM> _logger;
    private readonly string _instancePath;

    [ObservableProperty]
    private string? _patchPath;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public int UnmatchedCount { get; }

    public string OutputPath { get; }

    public string Message =>
        string.Format(
            Loc.Tr("Pack.Dialog.Message"),
            UnmatchedCount,
            OutputDirName);

    public bool HasPatch => !string.IsNullOrEmpty(PatchPath);

    public bool HasError =>
        !string.IsNullOrEmpty(ErrorMessage);

    public string PatchCreatedMessage =>
        HasPatch
            ? Loc.Tr("Pack.Dialog.PatchCreated_Message")
            : "";

    public event Action? Closed;

    public PatchArchiveDialogVM(
        PatchArchiveBuilder builder,
        IProcessLauncher launcher,
        ILogger<PatchArchiveDialogVM> logger,
        string instancePath,
        int unmatchedCount)
    {
        _builder = builder;
        _launcher = launcher;
        _logger = logger;
        _instancePath = instancePath;

        UnmatchedCount = unmatchedCount;
        OutputPath = Path.Combine(instancePath, OutputDirName);
    }

    [RelayCommand]
    private void Ignore()
    {
        _logger.LogDebug("Unmatched dialog: ignored by user");
        Closed?.Invoke();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        ErrorMessage = null;

        if (!Directory.Exists(OutputPath))
        {
            _logger.LogWarning(
                "Output folder does not exist: {Path}", OutputPath);
            ErrorMessage = string.Format(
                Loc.Tr("Pack.Dialog.FolderNotFound"),
                OutputPath);
            return;
        }

        try
        {
            _launcher.OpenFile(OutputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to open output folder: {Path}", OutputPath);
            ErrorMessage = string.Format(
                Loc.Tr("Pack.Dialog.FailedToOpenFolder"),
                ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreatePatch))]
    private async Task CreatePatchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        PatchPath = null;

        try
        {
            var path = await _builder.BuildAsync(
                _instancePath, CancellationToken.None);

            if (path is null)
            {
                _logger.LogWarning(
                    "PatchArchiveBuilder returned null — nothing to pack");
                ErrorMessage = Loc.Tr("Pack.Dialog.NothingToPack");
                return;
            }

            PatchPath = path;
            _logger.LogInformation(
                "Patch archive created via dialog: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create patch archive");
            ErrorMessage = string.Format(
                Loc.Tr("Pack.Dialog.FailedToCreatePatch"),
                ex.Message);
        }
        finally
        {
            IsBusy = false;
            CreatePatchCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCreatePatch() => !IsBusy;

    partial void OnPatchPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPatch));
        OnPropertyChanged(nameof(PatchCreatedMessage));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }
}
