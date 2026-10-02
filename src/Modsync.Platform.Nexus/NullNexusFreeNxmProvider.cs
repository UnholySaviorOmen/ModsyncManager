// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus;

/// <summary>
/// Default-реализация INexusFreeNxmProvider: всегда бросает.
///
/// Используется там, где нет UI. Free-скачивание требует
/// ModsyncManager.Gui (открытие браузера + ожидание nxm://).
/// </summary>
public sealed class NullNexusFreeNxmProvider : INexusFreeNxmProvider
{
    public Task<string?> RequestNxmUrlAsync(
        string game,
        int modId,
        int fileId,
        string displayName,
        CancellationToken ct)
    {
        throw new InvalidOperationException(
            "Free-загрузка с Nexus доступна только в GUI. " +
            "Используйте ModsyncManager.exe или задайте Premium API-key.");
    }
}
