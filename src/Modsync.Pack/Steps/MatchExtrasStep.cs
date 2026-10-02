// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Matching;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Сопоставляет файлы extras (из EntryScanResult) с содержимым архивов.
///
/// Полностью симметричен MatchExtensionsStep. Отдельный шаг — потому что
/// семантика разная (extensions относительно MO2/, extras относительно
/// Stock Game/), и захочется их независимо дорабатывать.
///
/// ArchiveMatcher.Build должен быть вызван до ExecuteAsync.
/// Это ответственность PackPipeline.
/// </summary>
public sealed class MatchExtrasStep
    : IStep<MatchExtrasStep.Input, MatchEntriesResult>
{
    private readonly ILogger<MatchExtrasStep> _logger;

    public MatchExtrasStep(ILogger<MatchExtrasStep> logger)
    {
        _logger = logger;
    }

    public Task<MatchEntriesResult> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (input.Matcher is null)
            throw new InvalidOperationException(
                "MatchExtrasStep.Input.Matcher must be set before ExecuteAsync.");

        var scan = input.Scan;

        _logger.LogInformation(
            "MatchExtrasStep: matching {Count} extra(s)",
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
            "MatchExtrasStep: {Total} file(s), {Matched} matched, {Unmatched} unmatched",
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

        internal ArchiveMatcher Matcher { get; init; } = null!;
    }
}
