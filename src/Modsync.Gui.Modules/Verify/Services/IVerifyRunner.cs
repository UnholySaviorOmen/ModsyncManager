// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Install.Verify;

namespace Modsync.Gui.Modules.Verify.Services;

/// <summary>
/// Обёртка над VerifyPipeline.
///
/// Зачем: VerifyVM тестируется без поднятия всего DI-графа pipeline.
/// В тестах IVerifyRunner заменяется fake-ом, который либо возвращает
/// готовый VerifyReport, либо бросает исключение.
///
/// VerifyPipeline.Execute — синхронный (решение №116). VerifyRunner
/// оборачивает его в Task.Run, чтобы не блокировать UI-поток.
///
/// Реализация (VerifyRunner) — тонкая. Вся логика — в pipeline.
/// </summary>
public interface IVerifyRunner
{
    /// <summary>
    /// Запустить верификацию. Возвращает отчёт.
    ///
    /// Бросает:
    ///   - OperationCanceledException (или AggregateException с cancellation)
    ///     при отмене через ct;
    ///   - ArgumentException — если targetPath пустой;
    ///   - любой exception из pipeline — при внутренней ошибке.
    ///
    /// «Checks failed» (report.IsOk == false) — НЕ exception,
    /// а нормальный результат.
    /// </summary>
    Task<VerifyReport> RunAsync(string targetPath, CancellationToken ct);
}
