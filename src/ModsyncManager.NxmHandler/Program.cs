// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using ModsyncManager.NxmHandler;

// ---------------------------------------------------------------------
//  ModsyncManager.NxmHandler
//
//  Системный callback для nxm:// URL. Windows вызывает этот exe,
//  когда пользователь кликает «Mod Manager Download» на Nexus
//  (после того, как Шаг B зарегистрировал handler в HKCU).
//
//  Логика:
//    1. Разобрать argv → nxm:// URL.
//    2. Отправить URL в работающий ModsyncManager через named pipe.
//    3. Завершиться с кодом 0/2/3.
//
//  Exit codes:
//    0 — URL передан в pipe.
//    2 — невалидный argv.
//    3 — pipe недоступен (ModsyncManager не запущен).
//
//  НЕ стартуем ModsyncManager.exe. Решение 256: старт GUI из handler-а
//  бессмысленен — активного install нет, URL теряется. Если pipe
//  недоступен — пользователь сам запускает ModsyncManager и повторяет.
//
//  Диагностика: каждое событие пишется в
//  %LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log.
//  Windows вызывает handler без консоли, stderr никто не видит —
//  файл остаётся единственным способом понять, что произошло.
//
//  Никаких исключений наружу: handler — системный callback.
//
//  Про top-level args: в top-level statements имя "args" зарезервировано
//  компилятором за массивом аргументов Main. Windows вызывает handler
//  как `ModsyncManager.NxmHandler.exe "%1"`, где %1 подставляется как
//  один аргумент — это и есть наш URL.
// ---------------------------------------------------------------------

var logger = new HandlerLogger();

try
{
    logger.Log($"argv: [{string.Join(", ", args)}]");

    var parsed = NxmHandlerArgs.TryParse(args);

    if (parsed is null)
    {
        const string message =
            "ModsyncManager.NxmHandler: expected a single nxm:// URL argument.";

        Console.Error.WriteLine(message);
        logger.Log("argv invalid: expected a single nxm:// URL argument");
        logger.Log("exit: 2");
        return 2;
    }

    logger.Log($"parsed: {parsed.NxmUrl}");

    // Пробуем pipe (ModsyncManager уже запущен).
    var sent = await PipeClient.TrySendAsync(
        parsed.NxmUrl, PipeClient.DefaultTimeout);

    if (sent)
    {
        logger.Log("pipe: OK");
        logger.Log("exit: 0");
        return 0;
    }

    // ModsyncManager не запущен (или pipe недоступен).
    // НЕ стартуем GUI — решение 256.
    const string notRunningMessage =
        "ModsyncManager is not running. " +
        "Start ModsyncManager.exe, then click \"Mod Manager Download\" again.";

    Console.Error.WriteLine(notRunningMessage);
    logger.Log("pipe: FAILED (timeout or unavailable)");
    logger.Log("exit: 3");
    return 3;
}
catch (Exception ex)
{
    // Последний рубеж: не должны сюда попасть, но если попали —
    // не показываем стектрейс пользователю.
    var message = $"ModsyncManager.NxmHandler: unexpected error: {ex.Message}";

    Console.Error.WriteLine(message);
    logger.Log($"unexpected error: {ex.Message}");
    logger.Log("exit: 3");
    return 3;
}
