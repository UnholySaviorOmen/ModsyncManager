// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Matching;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Сопоставляет файлы extensions (из EntryScanResult) с содержимым архивов.
///
/// Использует ArchiveMatcher, переданный в Input. Один ArchiveMatcher
/// должен переиспользоваться всеми Match*-шагами — иначе архивы будут
/// распаковываться повторно.
///
/// Логика — 1:1 с MatchStep:
///   1. exact match по (hash, relativePath);
///   2. иначе match по hash (первый кандидат);
///   3. иначе — в Unmatched.
///
/// НЕ пишет unmatched на диск. Это делает PackPipeline.
/// НЕ обрабатывает meta.ini (у extensions его нет).
///
/// ArchiveMatcher.Build должен быть вызван до ExecuteAsync.
/// Это ответственность PackPipeline.
/// </summary>
public sealed class MatchExtensionsStep
    : IStep<MatchExtensionsStep.Input, MatchEntriesResult>
{
    private readonly ILogger<MatchExtensionsStep> _logger;

    public MatchExtensionsStep(ILogger<MatchExtensionsStep> logger)
    {
        _logger = logger;
    }

    public Task<MatchEntriesResult> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (input.Matcher is null)
            throw new InvalidOperationException(
                "MatchExtensionsStep.Input.Matcher must be set before ExecuteAsync.");

        var scan = input.Scan;

        _logger.LogInformation(
            "MatchExtensionsStep: matching {Count} extension(s)",
            scan.TotalEntries);

        var directives = new Dictionary<string, IReadOnlyList<Directive>>(
            StringComparer.Ordinal);

        var unmatched = new List<UnmatchedEntry>();

        int totalFiles = 0;
        int matched = 0;

        foreach (var (entryName, files) in scan.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var list = new List<Directive>(files.Count);

            foreach (var file in files)
            {
                totalFiles++;

                var directive = input.Matcher.TryMatch(file);
                if (directive is not null)
                {
                    list.Add(directive);
                    matched++;
                    continue;
                }

                unmatched.Add(new UnmatchedEntry(
                    EntryName: entryName,
                    RelativePath: file.RelativePath,
                    Size: file.Size));
            }

            directives[entryName] = list;
        }

        _logger.LogInformation(
            "MatchExtensionsStep: {Total} file(s), {Matched} matched, {Unmatched} unmatched",
            totalFiles, matched, unmatched.Count);

        return Task.FromResult(new MatchEntriesResult
        {
            Directives = directives,
            Unmatched = unmatched,
        });
    }

    // ------------------------------------------------------------------
    //  Input
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required EntryScanResult Scan { get; init; }

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
