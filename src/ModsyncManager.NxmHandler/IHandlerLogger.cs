// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace ModsyncManager.NxmHandler;

/// <summary>
/// Логгер handler-а: пишет диагностические сообщения в файл.
///
/// Handler — системный callback, у него нет окна. Windows вызывает
/// его без консоли, stderr никто не видит. Файл — единственный
/// способ понять, что произошло при клике «Mod Manager Download».
///
/// Все методы НИКОГДА не бросают: handler не должен падать из-за
/// проблем с логом. Если запись не удалась — молча игнорируем.
///
/// Формат строки: `[2026-09-29 14:40:34.789] message`.
/// Кодировка: UTF-8 без BOM. Перевод строк: CRLF.
/// </summary>
public interface IHandlerLogger
{
    /// <summary>
    /// Записать одно сообщение в лог.
    /// Никогда не бросает.
    /// </summary>
    void Log(string message);

    /// <summary>
    /// Путь к файлу лога. Для диагностики и тестов.
    /// </summary>
    string LogFilePath { get; }
}
