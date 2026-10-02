// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Shared.Models;

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Сканирует &lt;exeDir&gt;/Instances/ и возвращает список готовых
/// к установке/установленных сборок.
///
/// Абстракция нужна для тестируемости HomeVM: в тестах
/// подставляется fake с готовым списком.
///
/// Синхронный: папка Instances/ маленькая (единицы подпапок),
/// файловые операции быстрые. Если окажется, что скан тормозит
/// (десятки инстансов на сетевом диске) — введём async.
/// </summary>
public interface IInstalledPackScanner
{
    /// <summary>
    /// Просканировать Instances/ и вернуть все валидные сборки.
    ///
    /// Что считаем валидным:
    ///   - папка в Instances/ существует,
    ///   - в ней есть modlist.json,
    ///   - modlist.json успешно парсится как ModlistManifest.
    ///
    /// Битые/отсутствующие манифесты — skip + log warning.
    /// Отсутствие самой папки Instances/ — пустой список
    /// (валидное состояние: приложение только что установлено).
    ///
    /// Результат отсортирован по Name (Ordinal).
    /// </summary>
    IReadOnlyList<InstalledPackInfo> Scan();
}
