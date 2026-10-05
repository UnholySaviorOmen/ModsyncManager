// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Core.Validation;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Pack;
using Modsync.Pack.Models;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// VM формы конфига Packer-а.
///
/// Два сценария:
///   1. Load config — форма открывается с заполненными полями
///      из существующего modsyncmanager-pack.json.
///   2. Create config — форма открывается пустой.
///
/// На выходе (Pack) сохраняет config на диск и поднимает
/// ConfigCreated с PackConfigBuilderInput.
///
/// Save as… сохраняет config, не запуская packer.
///
/// Пути сохранения:
///   - Loaded: _loadedConfigPath (тот же файл, откуда загрузили).
///   - Created: &lt;InstancePath&gt;/modsyncmanager-pack.json.
/// </summary>
public sealed partial class PackConfigVM : ViewModel
{
    private const string DefaultConfigFileName = "modsyncmanager-pack.json";

    private readonly PackConfigBuilder _builder;
    private readonly IFilePickerService _picker;
    private readonly ILogger<PackConfigVM> _logger;

    /// <summary>
    /// Путь к файлу, из которого загружен config, или null если
    /// форма создана с нуля. Используется для сохранения при Pack.
    /// </summary>
    private string? _loadedConfigPath;

    /// <summary>
    /// Archive sources из загруженного config, для которых нет
    /// соответствующего файла в downloads/. Не отображаются в UI,
    /// но сохраняются в BuildInput — чтобы при возврате файла в
    /// downloads/ source остался.
    /// </summary>
    private readonly Dictionary<string, PackArchiveSource> _orphanedSources =
        new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    //  ObservableProperty-поля
    // ------------------------------------------------------------------

    [ObservableProperty]
    private string? _instancePath;

    [ObservableProperty]
    private string _metaName = "";

    [ObservableProperty]
    private string _metaVersion = "";

    [ObservableProperty]
    private string _metaAuthor = "";

    [ObservableProperty]
    private string _selectedGame = "";

    [ObservableProperty]
    private string _gameVersion = "";

    [ObservableProperty]
    private string? _selectedProfile;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _loadedFromPath;

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

    public bool HasLoadedFrom => !string.IsNullOrEmpty(LoadedFromPath);

    // ------------------------------------------------------------------
    //  MO2 archive (hardcoded, display only)
    // ------------------------------------------------------------------

    public string Mo2VersionDisplay => PackConfigBuilder.Mo2Version;
    public string Mo2ArchiveDisplay => PackConfigBuilder.Mo2ArchiveName;
    public string Mo2UrlDisplay => PackConfigBuilder.Mo2Url;
    public string Mo2HashDisplay => PackConfigBuilder.Mo2Hash.ToString();

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

                return FormatSize(new FileInfo(path).Length);
            }
            catch
            {
                return "";
            }
        }
    }

    // ------------------------------------------------------------------
    //  Save path display / overwrite warning
    // ------------------------------------------------------------------

    /// <summary>
    /// Куда будет сохранён config при нажатии Pack.
    /// </summary>
    public string SavePathDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(_loadedConfigPath))
                return _loadedConfigPath;

            if (string.IsNullOrWhiteSpace(InstancePath))
                return $"<instance folder>/{DefaultConfigFileName}";

            return Path.Combine(InstancePath, DefaultConfigFileName);
        }
    }

    /// <summary>
    /// Предупреждение «overwrite existing config», показывается
    /// только для НЕ загруженного config (когда мы собираемся
    /// писать в дефолтное место, а там уже что-то есть).
    /// </summary>
    public string? OverwriteWarning
    {
        get
        {
            if (!string.IsNullOrEmpty(_loadedConfigPath))
                return null;

            if (string.IsNullOrWhiteSpace(InstancePath))
                return null;

            var path = Path.Combine(InstancePath, DefaultConfigFileName);
            if (!File.Exists(path))
                return null;

            return $"A config already exists at {path}. " +
                   $"Pressing Pack will overwrite it.";
        }
    }

    // ------------------------------------------------------------------
    //  Events
    // ------------------------------------------------------------------

    /// <summary>
    /// Срабатывает, когда пользователь нажал Pack. Config уже
    /// сохранён на диск. PackVM запускает pipeline.
    /// </summary>
    public event Action<PackConfigBuilderInput>? ConfigCreated;

    /// <summary>
    /// Срабатывает, когда пользователь нажал Cancel.
    /// </summary>
    public event Action? Cancelled;

    // ------------------------------------------------------------------
    //  Constructor
    // ------------------------------------------------------------------

    public PackConfigVM(
        PackConfigBuilder builder,
        IFilePickerService picker,
        ILogger<PackConfigVM> logger)
    {
        _builder = builder;
        _picker = picker;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  Public API for PackVM
    // ------------------------------------------------------------------

    /// <summary>
    /// Загрузить config из файла и заполнить форму.
    /// Вызывается из формы через LoadConfigFileCommand.
    ///
    /// При ошибке — ErrorMessage, форма остаётся пустой.
    /// </summary>
    public async Task LoadConfigAsync(string configPath, CancellationToken ct = default)
    {
        try
        {
            var config = await PackConfigJson.LoadAsync(configPath, ct);

            _loadedConfigPath = Path.GetFullPath(configPath);
            LoadedFromPath = _loadedConfigPath;

            // instance.path — относительный, резолвим от папки config.
            var configDir = Path.GetDirectoryName(_loadedConfigPath)
                ?? throw new InvalidOperationException(
                    $"Cannot determine config directory: {_loadedConfigPath}");

            var resolvedInstance = Path.GetFullPath(
                Path.Combine(configDir, config.Instance.Path));

            InstancePath = resolvedInstance;

            MetaName = config.Meta.Name;
            MetaVersion = config.Meta.Version;
            MetaAuthor = config.Meta.Author;
            SelectedGame = config.Meta.Game;
            GameVersion = config.Meta.GameVersion;

            // Сначала сканируем инстанс (создаём UnresolvedArchiveRowVM).
            await RefreshFromInstanceAsync();

            // Профиль мог быть другим, чем Default из списка — установим явно.
            SelectedProfile = config.Mo2.Profile;

            // Extensions/Extras.
            Extensions.Clear();
            foreach (var ext in config.Mo2.Extensions)
            {
                var vm = new PathEntryVM(ext, RemoveExtensionInternal);
                vm.ValidationChanged += OnChildValidationChanged;
                Extensions.Add(vm);
            }

            Extras.Clear();
            foreach (var extra in config.StockGame.Extras)
            {
                var vm = new PathEntryVM(extra, RemoveExtraInternal);
                vm.ValidationChanged += OnChildValidationChanged;
                Extras.Add(vm);
            }

            // archiveSources: заполняем url/hash у существующих row,
            // если имя архива совпало. Записи без файла в downloads/
            // сохраняем в _orphanedSources.
            ApplyArchiveSources(config.ArchiveSources);

            _logger.LogInformation(
                "Config loaded: {Path} ({Extensions} extensions, {Extras} extras, {Sources} archiveSources)",
                _loadedConfigPath, Extensions.Count, Extras.Count,
                config.ArchiveSources.Count);

            OnPropertyChanged(nameof(SavePathDisplay));
            OnPropertyChanged(nameof(OverwriteWarning));
            NotifyValidationChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config: {Path}", configPath);
            ErrorMessage = $"Failed to load config: {ex.Message}";
        }
    }

    private void ApplyArchiveSources(IReadOnlyList<PackArchiveSource> sources)
    {
        // Индекс: имя архива → row.
        var byName = UnresolvedArchives
            .ToDictionary(a => a.FileName, StringComparer.OrdinalIgnoreCase);

        _orphanedSources.Clear();

        foreach (var source in sources)
        {
            if (!byName.TryGetValue(source.Archive, out var row))
            {
                // Архива нет в downloads/ — сохраняем скрыто.
                _orphanedSources[source.Archive] = source;
                _logger.LogDebug(
                    "Orphaned archive source (not in downloads/): {Archive}",
                    source.Archive);
                continue;
            }

            row.LoadSources(source);
        }
    }

    // ------------------------------------------------------------------
    //  Commands
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task LoadConfigFileAsync()
    {
        var picked = await _picker.PickFileAsync(
            "Load pack config", ".json");

        if (string.IsNullOrWhiteSpace(picked))
            return;

        await LoadConfigAsync(picked);
    }

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

    // ------------------------------------------------------------------
    //  Pack
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task PackAsync()
    {
        ErrorMessage = null;

        var input = BuildInput();

        // Сохраняем config. Если save упал — ErrorMessage, событие
        // НЕ поднимаем, pack не запускается.
        var savedPath = await TrySaveConfigAsync(input);
        if (savedPath is null)
            return;

        _logger.LogInformation(
            "Pack Config: instance={Instance}, name={Name}, " +
            "archives={Count}, saved={Path}",
            input.InstancePath, input.Meta.Name,
            input.ArchiveSources.Count, savedPath);

        ConfigCreated?.Invoke(input);
    }

    private bool CanPack()
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

    // ------------------------------------------------------------------
    //  Save as…
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task SaveAsAsync()
    {
        var target = await _picker.SaveFileAsync(
            "Save pack config as",
            DefaultConfigFileName,
            ".json");

        if (string.IsNullOrWhiteSpace(target))
            return;

        ErrorMessage = null;

        try
        {
            var input = BuildInput();
            var config = _builder.Build(input);
            var json = PackConfigJson.Serialize(config);
            await File.WriteAllTextAsync(target, json);

            _logger.LogInformation(
                "Pack config saved as: {Path}", target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pack config");
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    //  Cancel
    // ------------------------------------------------------------------

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke();
    }

    // ------------------------------------------------------------------
    //  Internal save
    // ------------------------------------------------------------------

    /// <summary>
    /// Сохраняет config в _loadedConfigPath, либо в дефолтный
    /// &lt;InstancePath&gt;/modsyncmanager-pack.json.
    ///
    /// Возвращает путь сохранения при успехе, null при ошибке
    /// (ErrorMessage выставлен).
    /// </summary>
    private async Task<string?> TrySaveConfigAsync(PackConfigBuilderInput input)
    {
        var target = _loadedConfigPath
            ?? Path.Combine(InstancePath!, DefaultConfigFileName);

        try
        {
            var config = _builder.Build(input);
            var json = PackConfigJson.Serialize(config);
            await File.WriteAllTextAsync(target, json);

            return target;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to save config: {Path}", target);
            ErrorMessage = $"Failed to save config: {ex.Message}";
            return null;
        }
    }

    // ------------------------------------------------------------------
    //  Validation notifications
    // ------------------------------------------------------------------

    private void OnChildValidationChanged(object? sender, EventArgs e)
    {
        PackCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    partial void OnInstancePathChanged(string? value)
    {
        PackCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Mo2SizeDisplay));
        OnPropertyChanged(nameof(SavePathDisplay));
        OnPropertyChanged(nameof(OverwriteWarning));
    }

    partial void OnLoadedFromPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLoadedFrom));
    }

    partial void OnMetaNameChanged(string value) => NotifyValidationChanged();
    partial void OnMetaVersionChanged(string value) => NotifyValidationChanged();
    partial void OnMetaAuthorChanged(string value) => NotifyValidationChanged();
    partial void OnSelectedGameChanged(string value) => NotifyValidationChanged();
    partial void OnGameVersionChanged(string value) => NotifyValidationChanged();
    partial void OnSelectedProfileChanged(string? value) => NotifyValidationChanged();

    private void NotifyValidationChanged()
    {
        PackCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
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

            // Не сбрасываем SelectedProfile, если он уже установлен
            // (например, при Load config). Установим только если null.
            if (string.IsNullOrWhiteSpace(SelectedProfile) && profiles.Count > 0)
                SelectedProfile = profiles[0];

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
            OnPropertyChanged(nameof(OverwriteWarning));

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

        // Добавляем "осиротевшие" source-ы (не видны в UI, но
        // сохраняются в config).
        archiveSources.AddRange(_orphanedSources.Values);

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

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
