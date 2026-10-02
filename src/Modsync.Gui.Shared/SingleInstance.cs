// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Threading;

namespace Modsync.Gui.Shared;

/// <summary>
/// Защита от запуска второго экземпляра GUI.
///
/// Использует именованный mutex (<c>Local\ModsyncManager.Gui.SingleInstance</c>
/// по умолчанию). Mutex system-wide в пределах сессии пользователя;
/// <c>Global\</c> не используется — создание глобальных объектов требует
/// <c>SeCreateGlobalPrivilege</c>, которого у обычного пользователя нет.
///
/// Логика:
///   - Первый вызов с данным именем → <see cref="IsFirstInstance"/> == true.
///     Mutex удерживается до <see cref="Dispose"/>.
///   - Второй вызов с тем же именем (пока первый жив) → false.
///
/// Класс кросс-платформенный: на Windows mutex — kernel object, на Linux —
/// shared memory. Логика одинаковая.
///
/// Не делает никаких попыток активировать окно первого инстанса — это
/// ответственность вызывающего кода (см. <c>SingleInstanceDialog</c>
/// в ModsyncManager.Gui).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Имя mutex-а по умолчанию. Часть контракта между <c>Program.Main</c>
    /// и вызывающим кодом: все инстансы ModsyncManager.exe должны использовать
    /// одно и то же имя.
    /// </summary>
    public const string DefaultMutexName = "ModsyncManager.Gui.SingleInstance";

    private readonly Mutex _mutex;
    private bool _disposed;

    /// <summary>
    /// Создаёт (или открывает существующий) именованный mutex.
    /// </summary>
    /// <param name="mutexName">
    /// Имя без префикса <c>Local\</c> / <c>Global\</c>. Префикс
    /// <c>Local\</c> добавляется автоматически.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="mutexName"/> пустой / whitespace / длиннее
    /// допустимого / содержит недопустимые символы (проверяется BCL).
    /// </exception>
    public SingleInstance(string mutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException(
                "Mutex name must be non-empty.", nameof(mutexName));
        }

        // Local\ — префикс сессии. Один пользователь = одна сессия = один GUI.
        var name = "Local\\" + mutexName;

        _mutex = new Mutex(initiallyOwned: true, name: name, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>
    /// true, если этот экземпляр — первый (mutex создан нами).
    /// false, если кто-то уже держит mutex с таким именем.
    /// </summary>
    public bool IsFirstInstance { get; }

    /// <summary>
    /// Освобождает mutex. Идемпотентно.
    ///
    /// Если этот экземпляр — первый (<see cref="IsFirstInstance"/> == true),
    /// после Dispose следующий запуск снова станет первым.
    /// Если не первый — Dispose лишь закрывает handle, чужой mutex
    /// продолжает жить.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex не принадлежит нам (теоретически невозможно, но
                // не хотим падать на выходе). Игнорируем.
            }
        }

        _mutex.Dispose();
    }
}
