// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Абстракция над диалогом выбора файла/папки.
///
/// Живёт в Shared (без ссылки на Avalonia), реализуется в ModsyncManager.Gui
/// через StorageProvider. Это позволяет FilePickerVM и InstallVM
/// оставаться тестируемыми без UI.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Открыть диалог выбора файла. Возвращает null, если пользователь отменил.
    /// </summary>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="filterHint">Опциональная подсказка для фильтра (расширение).</param>
    Task<string?> PickFileAsync(string title, string? filterHint = null);

    /// <summary>
    /// Открыть диалог выбора папки. Возвращает null, если пользователь отменил.
    /// </summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Открыть диалог сохранения файла. Возвращает null, если пользователь отменил.
    /// </summary>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="suggestedName">Имя файла по умолчанию.</param>
    /// <param name="filterHint">Опциональная подсказка для фильтра (расширение).</param>
    Task<string?> SaveFileAsync(
        string title,
        string suggestedName,
        string? filterHint = null);
}
