// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Pack;

namespace Modsync.Gui.Modules.Pack.Services;

/// <summary>
/// Обёртка над PackPipeline.
///
/// Зачем: PackVM тестируется без поднятия всего DI-графа pipeline.
/// В тестах IPackRunner заменяется fake-ом, который либо возвращает
/// готовый PackSummary, либо бросает исключение.
///
/// Реализация (PackRunner) — тонкая. Вся логика — в pipeline.
/// </summary>
public interface IPackRunner
{
    /// <summary>
    /// Запустить pack. Возвращает сводку результата.
    ///
    /// Бросает:
    ///   - OperationCanceledException (или AggregateException с cancellation)
    ///     при отмене через ct;
    ///   - любой exception из pipeline — при ошибке.
    /// </summary>
    Task<PackSummary> RunAsync(
        string configPath,
        IProgress<StepProgress> progress,
        CancellationToken ct);
}
