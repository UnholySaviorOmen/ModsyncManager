// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Modsync.Core.Archives;
using Microsoft.Extensions.Logging;

namespace Modsync.Core.Archives.Extraction;

/// <summary>
/// Распаковка архивов через 7z.exe (7-Zip).
/// Поддерживает .7z, .zip, .rar, .tar, .gz, .bz2 и др.
///
/// 7z.exe и 7z.dll поставляются вместе с Modsync Manager в Assets/7z/.
/// Лицензия 7-Zip — GNU LGPL (https://www.7-zip.org/license.txt).
/// </summary>
public sealed class SevenZipExtractor : IArchiveExtractor
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    private readonly string _sevenZipExePath;
    private readonly ILogger<SevenZipExtractor> _logger;

    public SevenZipExtractor(ILogger<SevenZipExtractor> logger)
    {
        _logger = logger;
        _sevenZipExePath = ResolveSevenZipPath();
    }

    /// <summary>Путь к 7z.exe — для тестов и диагностики.</summary>
    public string SevenZipExePath => _sevenZipExePath;

    public bool CanExtract(string archivePath)
    {
        // 7z понимает все нужные нам форматы. Отсекаем только совсем очевидное.
        if (string.IsNullOrWhiteSpace(archivePath))
            return false;

        return ArchiveExtensions.IsArchive(archivePath);
    }

    public async Task<IReadOnlyList<string>> ExtractAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken ct)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException(
                $"Archive not found: {archivePath}", archivePath);

        if (!Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException(
                $"Destination directory not found: {destinationDirectory}");

        if (!File.Exists(_sevenZipExePath))
            throw new FileNotFoundException(
                $"7z.exe not found at expected location: {_sevenZipExePath}",
                _sevenZipExePath);

        // Аргументы: x <archive> -o<dest> -y -bso0 -bsp0
        var arguments =
            $"x \"{archivePath}\" -o\"{destinationDirectory}\" -y -bso0 -bsp0";

        var psi = new ProcessStartInfo
        {
            FileName = _sevenZipExePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_sevenZipExePath)!,
        };

        using var process = new Process { StartInfo = psi };

        _logger.LogDebug(
            "Running 7z: {Exe} {Args}", _sevenZipExePath, arguments);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start 7z.exe: {ex.Message}", ex);
        }

        // Асинхронное чтение stdout/stderr, чтобы не было deadlock
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // Ожидание завершения с таймаутом
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }

            if (ct.IsCancellationRequested)
                throw;

            throw new TimeoutException(
                $"7z.exe timed out after {ProcessTimeout.TotalMinutes} minutes: {archivePath}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var exitCode = process.ExitCode;

        if (exitCode != 0)
        {
            // Коды 7z: 0 = OK, 1 = warning, 2 = fatal, 7 = cmdline, 8 = memory
            var message =
                $"7z.exe failed with exit code {exitCode} for '{archivePath}'.\n" +
                $"stdout: {stdout}\n" +
                $"stderr: {stderr}";
            throw new InvalidOperationException(message);
        }

        // ExitCode 0 — успех. ExitCode 1 — warning (некоторые файлы пропущены).
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogWarning(
                "7z.exe stderr for '{Archive}': {Stderr}",
                Path.GetFileName(archivePath), stderr);
        }

        // Собираем список извлечённых файлов
        var destFullPath = Path.GetFullPath(destinationDirectory);
        var extractedFiles = Directory
            .EnumerateFiles(destFullPath, "*", SearchOption.AllDirectories)
            .Select(f => Path
                .GetRelativePath(destFullPath, f)
                .Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        _logger.LogDebug(
            "Extracted {Count} files from {Archive}",
            extractedFiles.Count, Path.GetFileName(archivePath));

        return extractedFiles;
    }

    /// <summary>
    /// Находит 7z.exe в Assets/7z/ рядом с основной сборкой.
    /// </summary>
    private static string ResolveSevenZipPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Assets", "7z", "7z.exe");

        return candidate;
    }
}
