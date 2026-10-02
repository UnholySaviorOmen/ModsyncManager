// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Matching;
using Modsync.Platform.MO2.Readers;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Сопоставляет файлы модов с файлами внутри архивов.
///
/// Использует общий ArchiveMatcher, переданный через Input.Matcher.
/// Это гарантирует, что архивы распаковываются один раз на весь pipeline
/// (раньше MatchStep создавал свой экземпляр — двойной extract).
///
/// Логика матчинга делегируется matcher-у:
///   1. exact match по (hash, path);
///   2. match по hash (первый кандидат);
///   3. иначе — null.
///
/// Ответственность MatchStep:
///   - foreach по модам;
///   - meta.ini (в корне мода) → MetaIniReader → ModMetas;
///   - unmatched → __ModsyncManager_Output/MO2/mods/<ModName>/<path>;
///   - возврат MatchResult.
///
/// Никаких base64-inline и orphan-лимитов: всё, что не восстановимо
/// из архивов, идёт в __ModsyncManager_Output — автор решает, делать ли патч.
///
/// ArchiveMatcher.Build должен быть вызван до ExecuteAsync.
/// Это ответственность PackPipeline.
/// </summary>
public sealed class MatchStep : IStep<MatchStep.Input, MatchResult>
{
    private const string MetaIniFileName = "meta.ini";
    private const int InlineDiagnosticsTopN = 20;

    /// <summary>
    /// Относительный путь до корня unmatched-модов
    /// внутри __ModsyncManager_Output/:
    ///   __ModsyncManager_Output/MO2/mods/&lt;ModName&gt;/&lt;relativePath&gt;
    /// Симметрично структуре инстанса.
    /// </summary>
    private const string ModsyncOutputModsRelPath = "MO2/mods";

    private readonly ILogger<MatchStep> _logger;

    public MatchStep(ILogger<MatchStep> logger)
    {
        _logger = logger;
    }

    public Task<MatchResult> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (input.Matcher is null)
            throw new InvalidOperationException(
                "MatchStep.Input.Matcher must be set before ExecuteAsync.");

        var outputModsRoot = PrepareModsyncOutput(input.ModsyncOutputPath);

        var matcher = input.Matcher;

        var modDirectives = new Dictionary<string, IReadOnlyList<Directive>>(
            StringComparer.Ordinal);
        var modMetas = new Dictionary<string, ModMeta>(StringComparer.Ordinal);
        var unmatched = new List<UnmatchedFile>();

        int totalFiles = 0;
        int matched = 0;
        int unmatchedCount = 0;
        int metaIniCount = 0;

        foreach (var (modName, files) in input.ModScan.Mods)
        {
            ct.ThrowIfCancellationRequested();

            var directives = new List<Directive>(files.Count);
            var modPath = Path.Combine(input.ModsPath, modName);

            foreach (var file in files)
            {
                totalFiles++;

                if (IsRootMetaIni(file.RelativePath))
                {
                    var metaIniPath = Path.Combine(modPath, MetaIniFileName);
                    var modMeta = MetaIniReader.TryRead(metaIniPath);
                    if (modMeta is not null)
                    {
                        modMetas[modName] = modMeta;
                        metaIniCount++;
                    }
                    continue;
                }

                var directive = matcher.TryMatch(file);
                if (directive is not null)
                {
                    directives.Add(directive);
                    matched++;
                    continue;
                }

                unmatchedCount++;
                unmatched.Add(new UnmatchedFile(
                    ModName: modName,
                    RelativePath: file.RelativePath,
                    Size: file.Size));

                WriteUnmatchedFile(
                    input.ModsPath, outputModsRoot, modName, file.RelativePath);
            }

            modDirectives[modName] = directives;
        }

        _logger.LogInformation(
            "Match complete: {Total} files, {Matched} matched, {Unmatched} unmatched, {MetaIni} meta.ini",
            totalFiles, matched, unmatchedCount, metaIniCount);

        if (unmatchedCount > 0)
        {
            _logger.LogInformation(
                "Unmatched files written to __ModsyncManager_Output: {Count} ({Root})",
                unmatchedCount, outputModsRoot);

            LogInlineDiagnostics(unmatched);
        }

        return Task.FromResult(new MatchResult
        {
            ModDirectives = modDirectives,
            Unmatched = unmatched,
            ModMetas = modMetas,
        });
    }

    // ------------------------------------------------------------------
    //  __ModsyncManager_Output
    // ------------------------------------------------------------------

    private string PrepareModsyncOutput(string modsyncOutputPath)
    {
        var outputModsRoot = Path.Combine(
            modsyncOutputPath,
            ModsyncOutputModsRelPath.Replace('/', Path.DirectorySeparatorChar));

        if (Directory.Exists(outputModsRoot))
        {
            try
            {
                Directory.Delete(outputModsRoot, recursive: true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to clean __ModsyncManager_Output: {outputModsRoot}", ex);
            }
        }

        Directory.CreateDirectory(outputModsRoot);

        _logger.LogInformation("Modsync output cleaned: {Path}", outputModsRoot);

        return outputModsRoot;
    }

    private void WriteUnmatchedFile(
        string modsRoot,
        string outputModsRoot,
        string modName,
        string relativePath)
    {
        var srcPath = Path.Combine(modsRoot, modName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var dstPath = Path.Combine(outputModsRoot, modName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            var dstDir = Path.GetDirectoryName(dstPath);
            if (!string.IsNullOrEmpty(dstDir))
                Directory.CreateDirectory(dstDir);

            File.Copy(srcPath, dstPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to write unmatched file '{Mod}/{Path}' to __ModsyncManager_Output",
                modName, relativePath);
        }
    }

    // ------------------------------------------------------------------
    //  Диагностика unmatched
    // ------------------------------------------------------------------

    /// <summary>
    /// Логирует топ-N unmatched-файлов и разбивку по модам.
    ///
    /// Классификация по причинам (path-not-found / hash-differs) удалена
    /// в 12.13.6: она требовала доступа к byPath-индексу ArchiveMatcher-а,
    /// а мы решили не расширять его публичную поверхность. Если автору
    /// сборки понадобится — сделаем отдельный диагностический отчёт
    /// в v0.2.0.
    /// </summary>
    private void LogInlineDiagnostics(IReadOnlyList<UnmatchedFile> records)
    {
        _logger.LogInformation(
            "Unmatched diagnostics: {Count} files across {Mods} mods",
            records.Count,
            records.Select(r => r.ModName).Distinct(StringComparer.Ordinal).Count());

        _logger.LogInformation("  --- top {N} ---", InlineDiagnosticsTopN);

        int shown = 0;
        foreach (var rec in records)
        {
            if (shown >= InlineDiagnosticsTopN) break;
            shown++;

            _logger.LogInformation(
                "  {Index}. {Mod} / {Path}",
                shown, rec.ModName, rec.RelativePath);
        }

        if (records.Count > shown)
        {
            _logger.LogInformation(
                "  ({More} more unmatched files not shown)",
                records.Count - shown);
        }

        _logger.LogInformation("  by mod:");

        var byMod = records
            .GroupBy(r => r.ModName, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in byMod)
        {
            _logger.LogInformation(
                "    {Mod,-40} : {Total,3}",
                g.Key, g.Count());
        }
    }

    private static bool IsRootMetaIni(string relativePath)
        => string.Equals(relativePath, MetaIniFileName, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    //  Input
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required ArchiveIndex ArchiveIndex { get; init; }
        public required ModScanResult ModScan { get; init; }
        public required string DownloadsPath { get; init; }
        public required string ModsPath { get; init; }
        public required string ModsyncOutputPath { get; init; }

        /// <summary>
        /// Общий ArchiveMatcher для всех Match*-шагов.
        /// Должен быть уже Build-нут.
        ///
        /// internal — потому что ArchiveMatcher тоже internal.
        /// Не required: required-член не может быть менее видимым,
        /// чем содержащий тип (CS9032). Проверка на null — в ExecuteAsync.
        /// </summary>
        internal ArchiveMatcher Matcher { get; init; } = null!;
    }
}
