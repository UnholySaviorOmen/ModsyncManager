// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus;

/// <summary>
/// Бросается, когда операция требует Nexus-аутентификации,
/// но ключа нет или он невалиден.
///
/// Клиенты (GUI) должны перехватить это исключение
/// и предложить пользователю войти.
///
/// Отличается от InvalidOperationException тем, что говорит о
/// конкретном классе проблем — «нет логина», а не «что-то сломалось».
/// </summary>
public sealed class NexusAuthenticationException : Exception
{
    public NexusAuthenticationException(string message)
        : base(message)
    {
    }

    public NexusAuthenticationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
