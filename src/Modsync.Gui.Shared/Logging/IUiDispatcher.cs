// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Logging;

/// <summary>
/// Абстракция над UI-диспетчером.
///
/// Post — fire-and-forget (для логов).
/// InvokeAsync — await-вариант (для кода, которому нужно дождаться
/// выполнения на UI-потоке).
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> func);
}
