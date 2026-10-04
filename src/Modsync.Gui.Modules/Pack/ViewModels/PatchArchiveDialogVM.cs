// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// VM диалога «Unmatched files» после pack.
///
/// Показывается, когда PackSummary.UnmatchedFiles > 0. Даёт пользователю
/// три опции: игнорировать, открыть папку __ModsyncManager_Output/,
/// или собрать patch-архив ModsyncManager_Output.zip в downloads/.
///
/// После Ignore или успешного создания архива — событие Closed.
/// PackVM ловит его и закрывает диалог.
/// </summary>
public sealed partial class PatchArchiveDialogVM : ViewModel
{
    private const string OutputDirName = "__ModsyncManager_Output";

    private readonly PatchArchiveBuilder _builder;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<PatchArchiveDialogVM> _logger;
    private readonly string _instancePath;

    // ------------------------------------------------------------------
    //  ObservableProperty-поля
    // ------------------------------------------------------------------

    [ObservableProperty]
    private string? _patchPath;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    // ------------------------------------------------------------------
    //  Производные
    // ------------------------------------------------------------------

    public int UnmatchedCount { get; }

    public string OutputPath { get; }

    public string Message =>
        $"Found {UnmatchedCount} unmatched file(s). " +
        $"They were written to {OutputDirName}/.";

    public bool HasPatch => !string.IsNullOrEmpty(PatchPath);

    public bool HasError =>
        !string.IsNullOrEmpty(ErrorMessage);

    public string PatchCreatedMessage =>
        HasPatch
            ? $"Patch archive created. On next pack, provide a download source " +
              $"for it or place a .meta file next to it."
            : "";

    // ------------------------------------------------------------------
    //  Events
    // ------------------------------------------------------------------

    /// <summary>
    /// Срабатывает, когда диалог должен закрыться.
    /// PackVM ловит и ставит IsPatchDialogOpen = false.
    /// </summary>
    public event Action? Closed;

    // ------------------------------------------------------------------
    //  Constructor
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    //  Commands
    // ------------------------------------------------------------------

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
            ErrorMessage = $"Folder not found: {OutputPath}";
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
            ErrorMessage = $"Failed to open folder: {ex.Message}";
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
                ErrorMessage = "Nothing to pack (output folder is empty).";
                return;
            }

            PatchPath = path;
            _logger.LogInformation(
                "Patch archive created via dialog: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create patch archive");
            ErrorMessage = $"Failed to create patch: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CreatePatchCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCreatePatch() => !IsBusy;

    // ------------------------------------------------------------------
    //  Property change notifications
    // ------------------------------------------------------------------

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
