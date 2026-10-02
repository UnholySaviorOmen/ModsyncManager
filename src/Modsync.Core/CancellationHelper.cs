// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core;

/// <summary>
/// Хелперы для определения «это отмена, а не ошибка».
///
/// Зачем: Parallel.ForEachAsync (и Parallel.ForEach) при отмене
/// собирает OperationCanceledException со всех воркеров в один
/// AggregateException. Прямой catch (OperationCanceledException)
/// его не ловит.
///
/// Слепо ловить AggregateException нельзя: он может содержать
/// смесь — часть воркеров отменились, часть упали с реальной
/// ошибкой (например, FileNotFoundException за миллисекунду
/// до отмены). IsCancellation возвращает true только если ВСЕ
/// внутренние исключения — отмены.
/// </summary>
public static class CancellationHelper
{
    /// <summary>
    /// true, если исключение — отмена (OperationCanceledException,
    /// либо AggregateException, все inner которого — отмены).
    /// </summary>
    public static bool IsCancellation(Exception ex)
    {
        if (ex is null)
            return false;

        if (ex is OperationCanceledException)
            return true;

        if (ex is AggregateException agg)
        {
            var flat = agg.Flatten().InnerExceptions;
            if (flat.Count == 0)
                return false;

            return flat.All(inner => inner is OperationCanceledException);
        }

        return false;
    }
}
