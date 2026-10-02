// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Заглушка IRegistryAccessor для платформ, где реестра Windows нет
/// (Linux, macOS, а также CI-прогоны, где притворяться Windows смысла нет).
///
/// Все методы бросают PlatformNotSupportedException. Реально эта
/// реализация не используется в проде: Modsync Manager — Windows-only
/// (см. решение 237), а на Windows регистрируется WindowsRegistryAccessor.
///
/// Заглушка нужна, чтобы DI-контейнер собирался на любой платформе:
/// иначе AddModsyncNxm на Linux упадёт при первом же резолве
/// IProtocolRegistrar → NexusFreeDownloadSettingsVM → SettingsVM → MainWindowVM.
///
/// Класс намеренно НЕ помечен [SupportedOSPlatform("windows")]:
/// его использует NxmServices, который компилируется под net8.0
/// (не net8.0-windows), и CA1416-варнинг был бы лишним шумом.
/// </summary>
public sealed class NullRegistryAccessor : IRegistryAccessor
{
    public string? GetValue(string keyPath, string? valueName)
        => throw new PlatformNotSupportedException(
            "Windows registry is not available on this platform.");

    public void SetValue(string keyPath, string? valueName, string value)
        => throw new PlatformNotSupportedException(
            "Windows registry is not available on this platform.");

    public void DeleteKey(string keyPath, bool recursive)
        => throw new PlatformNotSupportedException(
            "Windows registry is not available on this platform.");

    public void DeleteValue(string keyPath, string? valueName)
        => throw new PlatformNotSupportedException(
            "Windows registry is not available on this platform.");

    public bool KeyExists(string keyPath)
        => throw new PlatformNotSupportedException(
            "Windows registry is not available on this platform.");
}
