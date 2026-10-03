// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core.Models.Pack;
using Modsync.Core.Validation;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Pack;
using Modsync.Pack.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// VM формы «Create Pack Config».
///
/// Заполняется пользователем, на выходе даёт PackConfigBuilderInput.
/// Внешний код (PackVM) запускает packer через IPackRunner.
///
/// Форма встроена в экран Pack как отдельное состояние
/// (IsCreatingConfig в PackVM). Отдельного окна нет.
/// </summary>
public sealed partial class CreatePackConfigVM : ViewModel
{
    private readonly PackConfigBuilder _builder;
    private readonly IFilePickerService _picker;
    private readonly ILogger<CreatePackConfigVM> _logger;

    // ------------------------------------------------------------------
    //  ObservableProperty-поля
    // ------------------------------------------------------------------

    [ObservableProperty]
    private string? _instancePath;

    [ObservableProperty]
    private string _metaName = "";

    [ObservableProperty]
    private string _metaVersion = "0.1.0";

    [ObservableProperty]
    private string _metaAuthor = "";

    [ObservableProperty]
    private string _selectedGame = "skyrimspecialedition";

    [ObservableProperty]
    private string _gameVersion = "1.6.1170";

    [ObservableProperty]
    private string? _selectedProfile;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string? _errorMessage;

    // ------------------------------------------------------------------
    //  Коллекции
    // ------------------------------------------------------------------

    public ObservableCollection<string> AvailableProfiles { get; } = new();

    public ObservableCollection<PathEntryVM> Extensions { get; } = new();

    public ObservableCollection<PathEntryVM> Extras { get; } = new();

    public ObservableCollection<UnresolvedArchiveRowVM> UnresolvedArchives { get; } = new();

    public bool HasUnresolvedArchives => UnresolvedArchives.Count > 0;

    public string UnresolvedArchivesHeader =>
        $"Archives without metadata ({UnresolvedArchives.Count})";

    public bool HasProfiles => AvailableProfiles.Count > 0;

    public string ProfileHint => HasProfiles
        ? ""
        : "No profiles found in the selected instance.";

    // ------------------------------------------------------------------
    //  MO2 archive (hardcoded standard, shown in form)
    // ------------------------------------------------------------------

    public string Mo2VersionDisplay => PackConfigBuilder.Mo2Version;
    public string Mo2ArchiveDisplay => PackConfigBuilder.Mo2ArchiveName;
    public string Mo2UrlDisplay => PackConfigBuilder.Mo2Url;
    public string Mo2HashDisplay => PackConfigBuilder.Mo2Hash.ToString();

    /// <summary>
    /// Размер MO2-архива в downloads/, если он там есть.
    /// Пустая строка, если файла нет (тогда размер неизвестен).
    /// </summary>
    public string Mo2SizeDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstancePath))
                return "";

            var path = Path.Combine(
                InstancePath, "MO2", "downloads", PackConfigBuilder.Mo2ArchiveName);

            try
            {
                if (!File.Exists(path))
                    return "not in downloads/";

                var size = new FileInfo(path).Length;
                return FormatSize(size);
            }
            catch
            {
                return "";
            }
        }
    }

    // ------------------------------------------------------------------
    //  Events
    // ------------------------------------------------------------------

    public event Action<PackConfigBuilderInput>? ConfigCreated;
    public event Action? Cancelled;

    // ------------------------------------------------------------------
    //  Constructor
    // ------------------------------------------------------------------

    public CreatePackConfigVM(
        PackConfigBuilder builder,
        IFilePickerService picker,
        ILogger<CreatePackConfigVM> logger)
    {
        _builder = builder;
        _picker = picker;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  Commands
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseInstanceAsync()
    {
        var picked = await _picker.PickFolderAsync("Select MO2 instance folder");
        if (string.IsNullOrWhiteSpace(picked))
            return;

        InstancePath = picked;
        await RefreshFromInstanceAsync();
    }

    [RelayCommand]
    private async Task RescanDownloadsAsync()
    {
        if (string.IsNullOrWhiteSpace(InstancePath))
            return;

        await RefreshFromInstanceAsync();
    }

    [RelayCommand]
    private void AddExtension()
    {
        var vm = new PathEntryVM(onRemove: RemoveExtensionInternal);
        vm.ValidationChanged += OnChildValidationChanged;
        Extensions.Add(vm);
        OnChildValidationChanged(null, EventArgs.Empty);
    }

    private void RemoveExtensionInternal(PathEntryVM vm)
    {
        vm.ValidationChanged -= OnChildValidationChanged;
        Extensions.Remove(vm);
        OnChildValidationChanged(null, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddExtra()
    {
        var vm = new PathEntryVM(onRemove: RemoveExtraInternal);
        vm.ValidationChanged += OnChildValidationChanged;
        Extras.Add(vm);
        OnChildValidationChanged(null, EventArgs.Empty);
    }

    private void RemoveExtraInternal(PathEntryVM vm)
    {
        vm.ValidationChanged -= OnChildValidationChanged;
        Extras.Remove(vm);
        OnChildValidationChanged(null, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanCreateConfig))]
    private void CreateConfig()
    {
        var input = BuildInput();
        _logger.LogInformation(
            "Create Pack Config: instance={Instance}, name={Name}, archives={Count}",
            input.InstancePath, input.Meta.Name, input.ArchiveSources.Count);

        ConfigCreated?.Invoke(input);
    }

    [RelayCommand(CanExecute = nameof(CanCreateConfig))]
    private async Task SaveTemplateAsync()
    {
        var target = await _picker.SaveFileAsync(
            "Save pack config as",
            "modsyncmanager-pack.json",
            ".json");

        if (string.IsNullOrWhiteSpace(target))
            return;

        try
        {
            var input = BuildInput();
            var config = _builder.Build(input);
            var json = PackConfigJson.Serialize(config);
            await File.WriteAllTextAsync(target, json);

            _logger.LogInformation(
                "Pack config template saved: {Path}", target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pack config template");
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke();
    }

    // ------------------------------------------------------------------
    //  Validation
    // ------------------------------------------------------------------

    private bool CanCreateConfig()
    {
        if (string.IsNullOrWhiteSpace(InstancePath))
            return false;

        if (string.IsNullOrWhiteSpace(MetaName))
            return false;

        if (!NameValidator.Validate(MetaName).IsValid)
            return false;

        if (!SemverValidator.Validate(MetaVersion).IsValid)
            return false;

        if (string.IsNullOrWhiteSpace(MetaAuthor))
            return false;

        if (string.IsNullOrWhiteSpace(SelectedGame))
            return false;

        if (string.IsNullOrWhiteSpace(GameVersion))
            return false;

        if (HasProfiles && string.IsNullOrWhiteSpace(SelectedProfile))
            return false;

        foreach (var ext in Extensions)
            if (!ext.IsValid) return false;

        foreach (var extra in Extras)
            if (!extra.IsValid) return false;

        foreach (var arch in UnresolvedArchives)
            if (!arch.IsValid) return false;

        return true;
    }

    private void OnChildValidationChanged(object? sender, EventArgs e)
    {
        CreateConfigCommand.NotifyCanExecuteChanged();
        SaveTemplateCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------
    //  Property change notifications
    // ------------------------------------------------------------------

    partial void OnInstancePathChanged(string? value)
    {
        CreateConfigCommand.NotifyCanExecuteChanged();
        SaveTemplateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Mo2SizeDisplay));
    }

    partial void OnMetaNameChanged(string value) => NotifyValidationChanged();
    partial void OnMetaVersionChanged(string value) => NotifyValidationChanged();
    partial void OnMetaAuthorChanged(string value) => NotifyValidationChanged();
    partial void OnSelectedGameChanged(string value) => NotifyValidationChanged();
    partial void OnGameVersionChanged(string value) => NotifyValidationChanged();
    partial void OnSelectedProfileChanged(string? value) => NotifyValidationChanged();

    private void NotifyValidationChanged()
    {
        CreateConfigCommand.NotifyCanExecuteChanged();
        SaveTemplateCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------
    //  Refresh from instance
    // ------------------------------------------------------------------

    private async Task RefreshFromInstanceAsync()
    {
        if (string.IsNullOrWhiteSpace(InstancePath))
            return;

        IsScanning = true;
        ErrorMessage = null;

        try
        {
            var profiles = _builder.ListProfiles(InstancePath);
            AvailableProfiles.Clear();
            foreach (var p in profiles)
                AvailableProfiles.Add(p);

            SelectedProfile = profiles.Count > 0 ? profiles[0] : null;

            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(ProfileHint));

            IReadOnlyList<UnresolvedArchiveInfo> unresolved;
            try
            {
                unresolved = await _builder.ScanDownloadsAsync(
                    InstancePath, CancellationToken.None);
            }
            catch (DirectoryNotFoundException ex)
            {
                ErrorMessage = ex.Message;
                unresolved = Array.Empty<UnresolvedArchiveInfo>();
            }

            UnresolvedArchives.Clear();
            foreach (var info in unresolved)
            {
                var row = new UnresolvedArchiveRowVM(info);
                row.ValidationChanged += OnChildValidationChanged;
                UnresolvedArchives.Add(row);
            }

            OnPropertyChanged(nameof(HasUnresolvedArchives));
            OnPropertyChanged(nameof(UnresolvedArchivesHeader));
            OnPropertyChanged(nameof(Mo2SizeDisplay));

            _logger.LogInformation(
                "Instance scanned: {Profiles} profiles, {Archives} unresolved archives",
                AvailableProfiles.Count, UnresolvedArchives.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan instance");
            ErrorMessage = $"Failed to scan instance: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            NotifyValidationChanged();
        }
    }

    // ------------------------------------------------------------------
    //  Build input
    // ------------------------------------------------------------------

    private PackConfigBuilderInput BuildInput()
    {
        var extensions = Extensions
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Value.Trim())
            .ToList();

        var extras = Extras
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Value.Trim())
            .ToList();

        var archiveSources = UnresolvedArchives
            .Where(a => !a.IsExcluded)
            .Select(a => a.ToPackArchiveSource())
            .ToList();

        var meta = new PackMeta
        {
            Name = MetaName.Trim(),
            Version = MetaVersion.Trim(),
            Author = MetaAuthor.Trim(),
            Game = SelectedGame.Trim(),
            GameVersion = GameVersion.Trim(),
        };

        return new PackConfigBuilderInput
        {
            InstancePath = InstancePath!,
            Meta = meta,
            Profile = string.IsNullOrWhiteSpace(SelectedProfile)
                ? "Default"
                : SelectedProfile!,
            Extensions = extensions,
            Extras = extras,
            ArchiveSources = archiveSources,
        };
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
