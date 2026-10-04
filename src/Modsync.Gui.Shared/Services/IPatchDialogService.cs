// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Абстракция над модальным диалогом «Unmatched files».
///
/// Реализуется в ModsyncManager.Gui (exe), где есть доступ к
/// Avalonia Window и StorageProvider. VM (PackVM) вызывает метод
/// ShowAsync, не зная, что внутри Avalonia.
///
/// Зачем: PackVM в Modsync.Gui.Modules не может открыть Window
/// напрямую — библиотека не ссылается на exe. Abstraction
/// позволяет инжектить реализацию.
/// </summary>
public interface IPatchDialogService
{
    /// <summary>
    /// Показать модальный диалог с тремя кнопками.
    /// Возвращает Task, который завершится после закрытия диалога.
    /// </summary>
    Task ShowAsync(
        string instancePath,
        int unmatchedCount,
        CancellationToken ct = default);
}
