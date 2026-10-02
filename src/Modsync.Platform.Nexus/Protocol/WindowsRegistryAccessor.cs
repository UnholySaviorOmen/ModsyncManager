// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Реализация IRegistryAccessor через Microsoft.Win32.Registry.
///
/// Все операции — под HKEY_CURRENT_USER.
///
/// Класс помечен [SupportedOSPlatform("windows")] — компилятор
/// не будет ругаться CA1416 на каждом вызове Registry.*. Это
/// корректно: Registry существует только на Windows, и этот
/// класс создаётся только на Windows (см. DI-регистрацию и
/// PlatformRegistrar — тоже Windows-only).
///
/// Регистрация в DI-контейнере должна быть обёрнута в
/// RuntimeInformation.IsOSPlatform(OSPlatform.Windows) или
/// помечена [SupportedOSPlatform("windows")] в точке вызова.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryAccessor : IRegistryAccessor
{
    public string? GetValue(string keyPath, string? valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key is null)
                return null;

            var value = key.GetValue(valueName);
            return value as string;
        }
        catch
        {
            return null;
        }
    }

    public void SetValue(string keyPath, string? valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            keyPath, writable: true);

        if (key is null)
        {
            throw new IOException(
                $"Failed to create/open registry key: HKCU\\{keyPath}");
        }

        // RegistryValueKind.String — REG_SZ.
        // BCL объявляет valueName как string?, но SetValue(null, ...)
        // корректно пишет в (Default). Аннотация BCL неточная,
        // передаём valueName как есть.
        key.SetValue(valueName!, value, RegistryValueKind.String);
    }

    public void DeleteKey(string keyPath, bool recursive)
    {
        try
        {
            if (recursive)
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    keyPath, throwOnMissingSubKey: false);
            }
            else
            {
                Registry.CurrentUser.DeleteSubKey(
                    keyPath, throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Failed to delete registry key HKCU\\{keyPath}: " +
                $"{ex.Message}", ex);
        }
    }

    public void DeleteValue(string keyPath, string? valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            keyPath, writable: true);

        if (key is null)
            return;

        try
        {
            // DeleteValue(null, ...) удаляет значение (Default).
            // BCL объявляет name как string (не string?), хотя
            // null — валидное значение. Аннотация неточная,
            // передаём valueName! с комментарием.
            key.DeleteValue(valueName!, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Failed to delete registry value " +
                $"HKCU\\{keyPath}\\{valueName ?? "(Default)"}: {ex.Message}",
                ex);
        }
    }

    public bool KeyExists(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key is not null;
    }
}
