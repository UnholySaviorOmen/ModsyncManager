// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Models;
using Modsync.Platform.MO2.Readers;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack;

/// <summary>
/// Собирает <see cref="PackConfig"/> из пользовательского ввода для
/// GUI-формы «Create Pack Config». Это тот же config, который обычно
/// лежит в <c>modsyncmanager-pack.json</c> — просто создаётся
/// программно, без JSON-файла.
///
/// <para>
/// Используется в сценарии «Pack without config»: пользователь
/// заполняет форму, GUI генерирует временный config-файл, передаёт
/// его в обычный <see cref="PackPipeline.ExecuteAsync"/>. Pipeline
/// ничего не знает о происхождении config — для него это просто путь.
/// </para>
///
/// <para>
/// Также предоставляет вспомогательные методы для GUI:
/// сканирование downloads/ на предмет non-nexus архивов и список
/// доступных профилей MO2.
/// </para>
///
/// <para>
/// Хардкод mo2-секции (версия, URL, hash) — стандарт для всех
/// генерируемых манифестов. Захардкожен в этом классе. При
/// обновлении MO2 — менять здесь.
/// </para>
/// </summary>
public sealed class PackConfigBuilder
{
    // ------------------------------------------------------------------
    //  Хардкод mo2-секции
    // ------------------------------------------------------------------

    /// <summary>
    /// Версия MO2, для которой генерируются config-и.
    /// </summary>
    public const string Mo2Version = "2.5.2";

    /// <summary>
    /// Имя архива MO2 в downloads/.
    /// </summary>
    public const string Mo2ArchiveName = "Mod.Organizer-2.5.2.7z";

    /// <summary>
    /// Официальный GitHub-релиз MO2. Единственный поддерживаемый источник.
    /// </summary>
    public const string Mo2Url =
        "https://github.com/ModOrganizer2/modorganizer/releases/download/v2.5.2/Mod.Organizer-2.5.2.7z";

    /// <summary>
    /// xxHash64 официального архива MO2 2.5.2.
    /// </summary>
    public static readonly XxHash64Value Mo2Hash =
        XxHash64Value.Parse("xxh64:E574E05EB6C470AD");

    private const string DownloadsDirName = "downloads";
    private const string Mo2DirName = "MO2";
    private const string ProfilesDirName = "profiles";

    // ------------------------------------------------------------------
    //  Fields
    // ------------------------------------------------------------------

    private readonly IHashCache _hashCache;
    private readonly ILogger<PackConfigBuilder> _logger;

    public PackConfigBuilder(
        IHashCache hashCache,
        ILogger<PackConfigBuilder> logger)
    {
        _hashCache = hashCache;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  ScanDownloads
    // ------------------------------------------------------------------

    /// <summary>
    /// Сканирует &lt;instancePath&gt;/MO2/downloads/ и возвращает
    /// список архивов, для которых нет .meta-файла (или .meta есть,
    /// но невалидный — нет modID/fileID).
    ///
    /// Nexus-архивы (с валидным .meta) в результат НЕ попадают:
    /// packer определит их источник автоматически.
    ///
    /// Хеши считаются через IHashCache.
    ///
    /// Результат отсортирован по FileName (Ordinal).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// Если &lt;instancePath&gt;/MO2/downloads/ не существует.
    /// </exception>
    public Task<IReadOnlyList<UnresolvedArchiveInfo>> ScanDownloadsAsync(
        string instancePath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(instancePath))
            throw new ArgumentException(
                "Instance path must be non-empty.", nameof(instancePath));

        var fullInstance = Path.GetFullPath(instancePath);
        var downloadsPath = Path.Combine(fullInstance, Mo2DirName, DownloadsDirName);

        if (!Directory.Exists(downloadsPath))
            throw new DirectoryNotFoundException(
                $"downloads/ not found: {downloadsPath}");

        _logger.LogInformation(
            "Scanning downloads for non-nexus archives: {Path}",
            downloadsPath);

        var result = new List<UnresolvedArchiveInfo>();

        foreach (var file in Directory.EnumerateFiles(
            downloadsPath, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            if (!ArchiveExtensions.IsArchive(file))
                continue;

            if (file.EndsWith(ArchiveDownloadHelper.PartSuffix,
                StringComparison.OrdinalIgnoreCase))
                continue;

            var metaPath = file + ".meta";
            if (HasValidNexusMeta(metaPath))
                continue;

            XxHash64Value hash;
            long size;
            try
            {
                hash = _hashCache.GetOrCompute(file);
                size = new FileInfo(file).Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to hash {File} during downloads scan — skipping",
                    file);
                continue;
            }

            result.Add(new UnresolvedArchiveInfo
            {
                FileName = Path.GetFileName(file),
                FullPath = file,
                Size = size,
                Hash = hash,
            });
        }

        result.Sort(static (a, b) =>
            string.CompareOrdinal(a.FileName, b.FileName));

        _logger.LogInformation(
            "Found {Count} archive(s) without valid .meta in downloads/",
            result.Count);

        return Task.FromResult<IReadOnlyList<UnresolvedArchiveInfo>>(result);
    }

    // ------------------------------------------------------------------
    //  ListProfiles
    // ------------------------------------------------------------------

    /// <summary>
    /// Возвращает список доступных профилей MO2 в
    /// &lt;instancePath&gt;/MO2/profiles/.
    ///
    /// Результат отсортирован по имени (Ordinal). Возвращает пустой
    /// список, если папка profiles/ не существует.
    /// </summary>
    public IReadOnlyList<string> ListProfiles(string instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
            throw new ArgumentException(
                "Instance path must be non-empty.", nameof(instancePath));

        var fullInstance = Path.GetFullPath(instancePath);
        var profilesPath = Path.Combine(fullInstance, Mo2DirName, ProfilesDirName);

        if (!Directory.Exists(profilesPath))
        {
            _logger.LogDebug(
                "profiles/ does not exist: {Path}", profilesPath);
            return Array.Empty<string>();
        }

        var profiles = Directory.EnumerateDirectories(profilesPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        _logger.LogDebug(
            "Found {Count} profile(s) in {Path}",
            profiles.Count, profilesPath);

        return profiles;
    }

    // ------------------------------------------------------------------
    //  Build
    // ------------------------------------------------------------------

    /// <summary>
    /// Собирает <see cref="PackConfig"/> из пользовательского ввода.
    ///
    /// mo2-секция подставляется из хардкода (Mo2Version, Mo2ArchiveName,
    /// Mo2Url, Mo2Hash). Пользователь может изменить только profile —
    /// остальное фиксировано.
    ///
    /// Никакой валидации не делает: валидация — ответственность
    /// вызывающего кода (PackPipeline сам провалидирует через
    /// PackConfigValidator).
    /// </summary>
    public PackConfig Build(PackConfigBuilderInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        if (string.IsNullOrWhiteSpace(input.InstancePath))
            throw new ArgumentException(
                "InstancePath must be non-empty.", nameof(input));

        if (input.Meta is null)
            throw new ArgumentException(
                "Meta must not be null.", nameof(input));

        var profile = string.IsNullOrWhiteSpace(input.Profile)
            ? "Default"
            : input.Profile;

        _logger.LogInformation(
            "Building PackConfig for '{Name}' v{Version} (profile: {Profile})",
            input.Meta.Name, input.Meta.Version, profile);

        var mo2Source = new MirrorSourceRef
        {
            Url = Mo2Url,
            Hash = Mo2Hash,
        };

        return new PackConfig
        {
            Meta = input.Meta,
            Instance = new PackInstance
            {
                Path = ComputeInstanceRelativePath(input.InstancePath),
            },
            Mo2 = new PackMo2
            {
                Version = Mo2Version,
                Profile = profile,
                Archive = Mo2ArchiveName,
                Source = mo2Source,
                Extensions = input.Extensions,
            },
            StockGame = new PackStockGame
            {
                Extras = input.Extras,
            },
            ArchiveSources = input.ArchiveSources,
        };
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Проверяет, что рядом с архивом есть .meta-файл с валидными
    /// modID и fileID (Nexus-формат).
    /// </summary>
    private static bool HasValidNexusMeta(string metaPath)
    {
        if (!File.Exists(metaPath))
            return false;

        try
        {
            var meta = MetaIniReader.TryRead(metaPath);
            return meta is not null
                && meta.ModId.HasValue
                && meta.FileId.HasValue;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Вычисляет instance.path — относительный путь.
    ///
    /// В конфиге modsyncmanager-pack.json instance.path — это путь
    /// ОТНОСИТЕЛЬНО папки, где лежит config. Поскольку мы генерируем
    /// временный config, а инстанс — реальный путь, нельзя просто
    /// взять instancePath как есть — он абсолютный.
    ///
    /// Решение: используем "." — то есть «инстанс в той же папке, что
    /// и config». Когда GUI сохранит временный config в корень
    /// инстанса, "." будет корректным. Если GUI сохранит config в
    /// temp — pipeline получит "." и будет искать MO2/ в temp.
    ///
    /// Чтобы это работало, GUI ДОЛЖЕН сохранять временный config
    /// в корень инстанса, а не в %TEMP%. Иначе pipeline не найдёт
    /// MO2/, downloads/, mods/.
    ///
    /// Альтернатива: сделать instance.path абсолютным. PackConfigValidator
    /// запрещает абсолютные пути (см. InstancePathValidator). Значит —
    /// только "." плюс сохранение config в корень инстанса.
    /// </summary>
    private static string ComputeInstanceRelativePath(string instancePath)
    {
        // Всегда "." — config лежит рядом с инстансом.
        // GUI отвечает за то, чтобы временный config оказался в
        // корне инстанса, а не в temp.
        return ".";
    }
}
