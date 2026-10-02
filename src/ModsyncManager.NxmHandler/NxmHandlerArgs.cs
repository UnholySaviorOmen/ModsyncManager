// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace ModsyncManager.NxmHandler;

/// <summary>
/// Разобранные аргументы командной строки handler-а.
///
/// Windows вызывает handler как:
///     ModsyncManager.NxmHandler.exe "nxm://skyrimspecialedition/mods/3863/files/..."
///
/// То есть один позиционный аргумент — URL.
///
/// Если argv пуст или URL не начинается с "nxm://" — handler
/// завершается с кодом 2 (невалидный argv).
///
/// Никаких флагов. Никаких --help. Handler — не CLI, а системный
/// callback. Пользователь его не вызывает руками.
/// </summary>
public sealed record NxmHandlerArgs
{
    /// <summary>URL, полученный от Windows. Гарантированно "nxm://..."</summary>
    public required string NxmUrl { get; init; }

    /// <summary>
    /// Разбирает argv. Возвращает null, если argv невалиден.
    /// Никогда не бросает.
    /// </summary>
    public static NxmHandlerArgs? TryParse(string[] args)
    {
        if (args is null || args.Length == 0)
            return null;

        // Windows передаёт URL первым аргументом. Если аргументов
        // больше — игнорируем лишние (защита от экзотики).
        var url = args[0];

        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!url.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
            return null;

        return new NxmHandlerArgs { NxmUrl = url };
    }
}
