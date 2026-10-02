// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Services;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.ViewModels;

public enum NexusAccountStatus
{
    /// <summary>Ключа нет.</summary>
    NotLoggedIn,

    /// <summary>Ключ есть, валиден.</summary>
    LoggedIn,

    /// <summary>Ключ есть, но Nexus его отверг (401).</summary>
    InvalidSaved,

    /// <summary>Ключ есть, но проверить не удалось (сеть, 5xx).</summary>
    NetworkError,

    /// <summary>Идёт валидация — статус временно неизвестен.</summary>
    Checking,
}

/// <summary>
/// VM секции Nexus в Settings.
///
/// Управляет API-ключом: показывает статус, сохраняет, удаляет,
/// перепроверяет. Использует INexusApiKeyProvider (сохранение) и
/// INexusCredentialValidator (валидация).
///
/// Не делает валидацию в конструкторе: RefreshAsync вызывается
/// из SettingsView.axaml.cs при Loaded. Так тесты могут явно
/// дождаться результата, а показ не тратит ресурсы.
/// </summary>
public sealed partial class NexusSettingsVM : ViewModel
{
    private const string NexusApiKeyUrl =
        "https://www.nexusmods.com/users/myaccount?tab=api";

    private readonly INexusApiKeyProvider _keyProvider;
    private readonly INexusCredentialValidator _validator;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<NexusSettingsVM> _logger;

    [ObservableProperty]
    private NexusAccountStatus _status = NexusAccountStatus.NotLoggedIn;

    [ObservableProperty]
    private string? _userName;

    [ObservableProperty]
    private bool _isPremium;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _apiKeyInput;

    [ObservableProperty]
    private string? _maskedSavedKey;

    public string KeyFilePath => _keyProvider.KeyFilePath;

    // ------------------------------------------------------------------
    //  Производные свойства для UI
    // ------------------------------------------------------------------

    public bool IsLoggedIn => Status == NexusAccountStatus.LoggedIn;

    public bool HasSavedKey =>
        Status is NexusAccountStatus.LoggedIn
             or NexusAccountStatus.InvalidSaved
             or NexusAccountStatus.NetworkError;

    public string StatusText => Status switch
    {
        NexusAccountStatus.NotLoggedIn => "Not logged in",
        NexusAccountStatus.LoggedIn =>
            $"Logged in as {UserName ?? "<unknown>"} " +
            $"(premium: {(IsPremium ? "yes" : "no")})",
        NexusAccountStatus.InvalidSaved => "Saved key is invalid or revoked",
        NexusAccountStatus.NetworkError => "Could not reach Nexus",
        NexusAccountStatus.Checking => "Checking...",
        _ => "Unknown",
    };

    public string StatusColor => Status switch
    {
        NexusAccountStatus.LoggedIn => "#7fc98a",
        NexusAccountStatus.InvalidSaved => "#d97777",
        NexusAccountStatus.NetworkError => "#d3b181",
        _ => "#a0a0a0",
    };

    public NexusSettingsVM(
        INexusApiKeyProvider keyProvider,
        INexusCredentialValidator validator,
        IProcessLauncher launcher,
        ILogger<NexusSettingsVM> logger)
    {
        _keyProvider = keyProvider;
        _validator = validator;
        _launcher = launcher;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  Refresh / revalidate
    // ------------------------------------------------------------------

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var key = _keyProvider.TryGetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            MaskedSavedKey = null;
            UserName = null;
            IsPremium = false;
            ErrorMessage = null;
            Status = NexusAccountStatus.NotLoggedIn;
            return;
        }

        MaskedSavedKey = MaskKey(key);
        await ValidateSavedKeyAsync(key);
    }

    [RelayCommand]
    private async Task RevalidateAsync()
    {
        var key = _keyProvider.TryGetApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            await RefreshAsync();
            return;
        }

        await ValidateSavedKeyAsync(key);
    }

    private async Task ValidateSavedKeyAsync(string key)
    {
        var previousStatus = Status;
        var previousUserName = UserName;
        var previousIsPremium = IsPremium;

        Status = NexusAccountStatus.Checking;
        ErrorMessage = null;

        try
        {
            var result = await _validator.ValidateAsync(key, CancellationToken.None);

            switch (result.Status)
            {
                case NexusKeyStatus.Valid:
                    UserName = result.UserName;
                    IsPremium = result.IsPremium;
                    ErrorMessage = null;
                    Status = NexusAccountStatus.LoggedIn;
                    break;

                case NexusKeyStatus.Invalid:
                    UserName = null;
                    IsPremium = false;
                    ErrorMessage = result.ErrorMessage;
                    Status = NexusAccountStatus.InvalidSaved;
                    break;

                case NexusKeyStatus.NetworkError:
                    // Сохраняем последние известные UserName/IsPremium —
                    // они могли быть из предыдущей успешной валидации.
                    UserName = previousUserName;
                    IsPremium = previousIsPremium;
                    ErrorMessage = result.ErrorMessage;
                    Status = NexusAccountStatus.NetworkError;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validation failed unexpectedly");
            ErrorMessage = ex.Message;
            Status = NexusAccountStatus.NetworkError;
        }
    }

    // ------------------------------------------------------------------
    //  Save & validate
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanSaveAndValidate))]
    private async Task SaveAndValidateAsync()
    {
        var key = ApiKeyInput?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return;

        Status = NexusAccountStatus.Checking;
        ErrorMessage = null;

        try
        {
            var result = await _validator.ValidateAsync(key, CancellationToken.None);

            switch (result.Status)
            {
                case NexusKeyStatus.Valid:
                    _keyProvider.Save(key);
                    ApiKeyInput = null;
                    MaskedSavedKey = MaskKey(key);
                    UserName = result.UserName;
                    IsPremium = result.IsPremium;
                    ErrorMessage = null;
                    Status = NexusAccountStatus.LoggedIn;

                    _logger.LogInformation(
                        "Saved Nexus API key for {UserName}",
                        result.UserName ?? "<unknown>");
                    break;

                case NexusKeyStatus.Invalid:
                    ErrorMessage = result.ErrorMessage
                        ?? "Nexus rejected the API key.";
                    Status = NexusAccountStatus.NotLoggedIn;
                    break;

                case NexusKeyStatus.NetworkError:
                    // В GUI не спрашиваем «сохранить всё равно?» —
                    // просто показываем ошибку, пользователь повторит.
                    ErrorMessage = result.ErrorMessage
                        ?? "Could not reach Nexus. Try again.";
                    Status = NexusAccountStatus.NotLoggedIn;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Save-and-validate failed");
            ErrorMessage = ex.Message;
            Status = NexusAccountStatus.NotLoggedIn;
        }
    }

    private bool CanSaveAndValidate() =>
        !string.IsNullOrWhiteSpace(ApiKeyInput);

    // ------------------------------------------------------------------
    //  Logout
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSavedKey))]
    private void Logout()
    {
        try
        {
            _keyProvider.Clear();
            _logger.LogInformation("Removed Nexus API key");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove Nexus API key");
            ErrorMessage = $"Failed to remove key: {ex.Message}";
        }

        ApiKeyInput = null;
        MaskedSavedKey = null;
        UserName = null;
        IsPremium = false;
        Status = NexusAccountStatus.NotLoggedIn;

        // ErrorMessage мог быть установлен выше — обнуляем только
        // если он не от ошибки удаления.
        if (ErrorMessage is not null
            && !ErrorMessage.StartsWith("Failed to remove", StringComparison.Ordinal))
        {
            ErrorMessage = null;
        }
    }

    // ------------------------------------------------------------------
    //  Open page
    // ------------------------------------------------------------------

    [RelayCommand]
    private void OpenNexusApiPage()
    {
        try
        {
            _launcher.OpenFile(NexusApiKeyUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to open Nexus API page in browser");
            ErrorMessage = $"Failed to open browser: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    partial void OnApiKeyInputChanged(string? value)
    {
        SaveAndValidateCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusChanged(NexusAccountStatus value)
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(HasSavedKey));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));

        LogoutCommand.NotifyCanExecuteChanged();
    }

    private static string MaskKey(string key)
    {
        if (key.Length < 12)
            return new string('*', key.Length);

        return $"{key[..4]}...{key[^4..]}";
    }
}
