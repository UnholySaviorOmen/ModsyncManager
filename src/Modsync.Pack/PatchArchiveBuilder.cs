// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack;

/// <summary>
/// Собирает patch-архив из содержимого __ModsyncManager_Output/.
///
/// Зачем: после pack unmatched-файлы (моды, extensions, extras) попадают
/// в __ModsyncManager_Output/. Без patch-архива пользователь не понимает,
/// что с ними делать. PatchArchiveBuilder упаковывает их в
/// ModsyncManager_Output.zip и кладёт в MO2/downloads/ — при следующем
/// pack архив будет виден как обычный unresolved-архив, пользователь
/// укажет для него source, и файлы разложатся через обычный pipeline
/// (installer берёт файлы из архива по хешу, независимо от их
/// расположения внутри).
///
/// Структура архива — 1:1 от __ModsyncManager_Output/, минус
/// modlist.json (это манифест, не unmatched-файл).
///
/// Пример:
///   ModsyncManager_Output.zip
///     MO2/mods/Actor Limit Fix/...
///     MO2/plugins/fomod.dll
///     MO2/tools/MyPatcher/MyPatcher.exe
///     Stock Game/skse64_loader.exe
///
/// Никаких префиксов _mods/_extensions/_extras не нужно: пути
/// естественно разные (mods/ vs корень MO2/ vs Stock Game/).
///
/// __ModsyncManager_Output/ после сборки НЕ удаляется — пользователь
/// решает сам, что с ним делать.
/// </summary>
public sealed class PatchArchiveBuilder
{
    private const string OutputDirName = "__ModsyncManager_Output";
    private const string Mo2DirName = "MO2";
    private const string DownloadsDirName = "downloads";
    private const string ManifestFileName = "modlist.json";

    /// <summary>
    /// Имя создаваемого patch-архива. Фиксированное — перезапись при
    /// повторном вызове. Пользователь сам решает, когда обновлять.
    /// </summary>
    public const string PatchArchiveName = "ModsyncManager_Output.zip";

    private readonly ILogger<PatchArchiveBuilder> _logger;

    public PatchArchiveBuilder(ILogger<PatchArchiveBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Собирает patch-архив.
    ///
    /// Возвращает полный путь к созданному zip, либо null, если
    /// собирать нечего:
    ///   - __ModsyncManager_Output/ не существует,
    ///   - __ModsyncManager_Output/ пуст,
    ///   - в нём только modlist.json (нечего паковать).
    ///
    /// Бросает ArgumentException, если instancePath пустой.
    /// Бросает OperationCanceledException при отмене.
    /// Бросает IOException при ошибке создания zip.
    /// </summary>
    public Task<string?> BuildAsync(
        string instancePath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(instancePath))
            throw new ArgumentException(
                "Instance path must be non-empty.", nameof(instancePath));

        var fullInstance = Path.GetFullPath(instancePath);
        var outputPath = Path.Combine(fullInstance, OutputDirName);

        if (!Directory.Exists(outputPath))
        {
            _logger.LogDebug(
                "PatchArchiveBuilder: {Path} does not exist — nothing to pack.",
                outputPath);
            return Task.FromResult<string?>(null);
        }

        // Собираем список файлов, исключая modlist.json в корне.
        var files = Directory.EnumerateFiles(
            outputPath, "*", SearchOption.AllDirectories)
            .Where(f => !IsManifestInRoot(outputPath, f))
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation(
                "PatchArchiveBuilder: {Path} has no files (only manifest or empty) — nothing to pack.",
                outputPath);
            return Task.FromResult<string?>(null);
        }

        var downloadsPath = Path.Combine(
            fullInstance, Mo2DirName, DownloadsDirName);

        if (!Directory.Exists(downloadsPath))
        {
            Directory.CreateDirectory(downloadsPath);
            _logger.LogDebug(
                "PatchArchiveBuilder: created downloads/ at {Path}",
                downloadsPath);
        }

        var archivePath = Path.Combine(downloadsPath, PatchArchiveName);

        _logger.LogInformation(
            "PatchArchiveBuilder: packing {Count} file(s) from {Source} into {Target}",
            files.Count, outputPath, archivePath);

        try
        {
            // ZipFile.Open с ZipArchiveMode.Create НЕ перезаписывает
            // существующий файл — падает с IOException. Удаляем вручную.
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(outputPath, file)
                    .Replace('\\', '/');

                zip.CreateEntryFromFile(file, relativePath);
            }
        }
        catch
        {
            // Удаляем частично созданный архив при любой ошибке.
            TryDelete(archivePath);
            throw;
        }

        var size = new FileInfo(archivePath).Length;

        _logger.LogInformation(
            "PatchArchiveBuilder: archive created at {Path} ({Size} bytes)",
            archivePath, size);

        return Task.FromResult<string?>(archivePath);
    }

    /// <summary>
    /// Проверяет, является ли файл манифестом в корне
    /// __ModsyncManager_Output/ (modlist.json). Манифесты во вложенных
    /// папках (если такие появятся) не считаются — только корневой.
    /// </summary>
    private static bool IsManifestInRoot(string outputRoot, string filePath)
    {
        var relative = Path.GetRelativePath(outputRoot, filePath);
        return string.Equals(relative, ManifestFileName, StringComparison.OrdinalIgnoreCase);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete partial archive: {Path}", path);
        }
    }
}
