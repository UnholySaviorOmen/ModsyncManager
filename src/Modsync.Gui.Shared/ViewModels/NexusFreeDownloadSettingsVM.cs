// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM секции «Nexus Free Download» в Settings.
///
/// Управляет регистрацией nxm:// handler-а:
///   - показывает текущий статус (наш / чужой / нет);
///   - RegisterCommand — регистрирует наш handler
///     (с backup предыдущего);
///   - RestoreCommand — восстанавливает предыдущий handler
///     из backup-файла;
///   - RefreshCommand — перечитывает статус из реестра.
///
/// Разделение Refresh:
///   - RefreshAsync (публичный) — сбрасывает ErrorMessage
///     и перечитывает State. Вызывается из View при Loaded.
///   - RefreshStateCore (private) — только перечитывает State,
///     не трогает ErrorMessage. Вызывается из finally в
///     Register/Restore, чтобы не затирать установленную ошибку.
/// </summary>
public sealed partial class NexusFreeDownloadSettingsVM : ViewModel
{
    private readonly IProtocolRegistrar _registrar;
    private readonly ILogger<NexusFreeDownloadSettingsVM> _logger;

    [ObservableProperty]
    private ProtocolRegistrationState _state =
        ProtocolRegistrationState.NotRegistered;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    // ------------------------------------------------------------------
    //  Производные свойства для UI
    // ------------------------------------------------------------------

    public bool IsRegisteredToUs =>
        State == ProtocolRegistrationState.RegisteredToUs;

    public bool IsRegisteredToOther =>
        State == ProtocolRegistrationState.RegisteredToOther;

    public bool IsNotRegistered =>
        State == ProtocolRegistrationState.NotRegistered;

    public bool IsUnknown =>
        State == ProtocolRegistrationState.Unknown;

    public string StatusText => State switch
    {
        ProtocolRegistrationState.NotRegistered =>
            "Free download is disabled. " +
            "Enable it to register the ModsyncManager nxm:// handler.",

        ProtocolRegistrationState.RegisteredToUs =>
            "Free download is enabled. " +
            "ModsyncManager handles all nxm:// links.",

        ProtocolRegistrationState.RegisteredToOther =>
            "Another app handles nxm:// links " +
            "(Vortex, Mod Organizer, or similar). " +
            "Enabling will replace it — you can restore it later.",

        ProtocolRegistrationState.Unknown =>
            "Could not read the current nxm:// handler state.",

        _ => "Unknown state.",
    };

    public string StatusColor => State switch
    {
        ProtocolRegistrationState.RegisteredToUs => "#7fc98a",
        ProtocolRegistrationState.RegisteredToOther => "#d3b181",
        ProtocolRegistrationState.Unknown => "#d97777",
        _ => "#a0a0a0",
    };

    public string HandlerPath { get; }

    // ------------------------------------------------------------------
    //  Constructor
    // ------------------------------------------------------------------

    public NexusFreeDownloadSettingsVM(
        IProtocolRegistrar registrar,
        ILogger<NexusFreeDownloadSettingsVM> logger)
    {
        _registrar = registrar;
        _logger = logger;

        HandlerPath = Path.Combine(
            AppContext.BaseDirectory,
            "ModsyncManager.NxmHandler.exe");
    }

    // ------------------------------------------------------------------
    //  Refresh
    // ------------------------------------------------------------------

    /// <summary>
    /// Публичный Refresh: сбрасывает ErrorMessage, перечитывает State.
    /// Вызывается из View при Loaded и из RefreshCommand.
    /// </summary>
    [RelayCommand]
    public Task RefreshAsync()
    {
        ErrorMessage = null;
        RefreshStateCore();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Внутренний Refresh: только State, без ErrorMessage.
    /// Вызывается из finally в Register/Restore.
    /// </summary>
    private void RefreshStateCore()
    {
        try
        {
            State = _registrar.GetState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read nxm:// registration state");
            State = ProtocolRegistrationState.Unknown;
        }
    }

    // ------------------------------------------------------------------
    //  Register
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task RegisterAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await Task.Run(() => _registrar.Register());

            switch (result.Status)
            {
                case ProtocolRegistrationStatus.Registered:
                case ProtocolRegistrationStatus.AlreadyRegistered:
                    if (result.PreviousHandlerPath is not null)
                    {
                        _logger.LogInformation(
                            "nxm:// handler registered. " +
                            "Previous handler backed up: {Previous}",
                            result.PreviousHandlerPath);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "nxm:// handler registered.");
                    }
                    break;

                case ProtocolRegistrationStatus.HandlerNotFound:
                    ErrorMessage =
                        "ModsyncManager.NxmHandler.exe not found next to " +
                        "ModsyncManager.exe. Reinstall ModsyncManager.";
                    break;

                case ProtocolRegistrationStatus.BackupFailed:
                case ProtocolRegistrationStatus.RegistryWriteFailed:
                case ProtocolRegistrationStatus.RegistryReadFailed:
                    ErrorMessage = result.Message
                        ?? "Failed to register nxm:// handler.";
                    break;

                default:
                    ErrorMessage = $"Unexpected status: {result.Status}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Register");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshStateCore();
        }
    }

    // ------------------------------------------------------------------
    //  Restore
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task RestoreAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await Task.Run(() => _registrar.Restore());

            switch (result.Status)
            {
                case ProtocolRegistrationStatus.Restored:
                    if (result.PreviousHandlerPath is not null)
                    {
                        _logger.LogInformation(
                            "nxm:// handler restored: {Previous}",
                            result.PreviousHandlerPath);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "nxm:// handler restored.");
                    }
                    break;

                case ProtocolRegistrationStatus.AlreadyRestored:
                    _logger.LogInformation(
                        "No backup to restore — our handler removed.");
                    break;

                case ProtocolRegistrationStatus.RegistryWriteFailed:
                case ProtocolRegistrationStatus.RegistryReadFailed:
                    ErrorMessage = result.Message
                        ?? "Failed to restore nxm:// handler.";
                    break;

                default:
                    ErrorMessage = $"Unexpected status: {result.Status}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Restore");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshStateCore();
        }
    }

    // ------------------------------------------------------------------
    //  Property change notifications
    // ------------------------------------------------------------------

    partial void OnStateChanged(ProtocolRegistrationState value)
    {
        OnPropertyChanged(nameof(IsRegisteredToUs));
        OnPropertyChanged(nameof(IsRegisteredToOther));
        OnPropertyChanged(nameof(IsNotRegistered));
        OnPropertyChanged(nameof(IsUnknown));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }
}
