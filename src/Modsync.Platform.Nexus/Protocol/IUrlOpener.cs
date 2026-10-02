// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Абстракция над открытием URL в программе по умолчанию
/// (обычно — браузере).
///
/// Реализация: ShellUrlOpener — через Process.Start с
/// UseShellExecute = true. Windows сам разберётся, что делать
/// с https:// — откроет браузер пользователя.
///
/// Абстракция нужна для тестов: fake IUrlOpener не открывает
/// реальный браузер, а записывает вызовы.
/// </summary>
public interface IUrlOpener
{
    /// <summary>
    /// Открывает URL. Бросает исключение при ошибке запуска.
    /// </summary>
    void Open(string url);
}
