// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Progress;
using Modsync.Pack;
using Modsync.Pack.Models;

namespace Modsync.Gui.Modules.Pack.Services;

/// <summary>
/// Обёртка над PackPipeline.
///
/// Зачем: PackVM тестируется без поднятия всего DI-графа pipeline.
/// В тестах IPackRunner заменяется fake-ом, который либо возвращает
/// готовый PackSummary, либо бросает исключение.
///
/// Реализация (PackRunner) — тонкая. Вся логика — в pipeline.
///
/// Единственный путь запуска pack — из формы Pack Config.
/// PackVM получает готовый PackConfigBuilderInput через событие
/// ConfigCreated от PackConfigVM и передаёт его сюда.
/// </summary>
public interface IPackRunner
{
    /// <summary>
    /// Запустить pack из формы Pack Config.
    ///
    /// Реализация:
    ///   1. Собирает PackConfig через PackConfigBuilder.Build(input).
    ///   2. Пишет временный config-файл в корень инстанса
    ///      (&lt;instancePath&gt;/modsyncmanager-pack.temp.json).
    ///      Именно в корень инстанса, а не в %TEMP% — потому что
    ///      config.instance.path == "." и pipeline ищет MO2/, downloads/
    ///      относительно папки config.
    ///   3. Запускает PackPipeline.
    ///   4. Удаляет temp-файл в finally.
    ///
    /// Бросает:
    ///   - OperationCanceledException (или AggregateException
    ///     с cancellation) при отмене через ct;
    ///   - любой exception из pipeline — при ошибке.
    /// </summary>
    Task<PackSummary> RunFromConfigBuilderAsync(
        PackConfigBuilderInput input,
        IProgress<StepProgress> progress,
        CancellationToken ct);
}
