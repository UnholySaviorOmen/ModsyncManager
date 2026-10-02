// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Install;

namespace Modsync.Gui.Modules.Install.Services;

/// <summary>
/// Обёртка над InstallPipeline.
///
/// Зачем: InstallVM тестируется без поднятия всего DI-графа pipeline.
/// В тестах IInstallRunner заменяется fake-ом, который либо возвращает
/// готовый InstallSummary, либо бросает исключение.
///
/// Реализация (InstallRunner) — тонкая. Вся логика — в pipeline.
/// </summary>
public interface IInstallRunner
{
    /// <summary>
    /// Запустить установку. Возвращает сводку результата.
    ///
    /// target == null → InstallInputFactory сам разрулит в
    /// &lt;exeDir&gt;/Instances/&lt;normalize(meta.name)&gt;/.
    ///
    /// Бросает:
    ///   - OperationCanceledException (или AggregateException с cancellation)
    ///     при отмене через ct;
    ///   - любой exception из pipeline — при ошибке.
    /// </summary>
    Task<InstallSummary> RunAsync(
        string manifestPath,
        string? target,
        IProgress<StepProgress> progress,
        CancellationToken ct);
}
