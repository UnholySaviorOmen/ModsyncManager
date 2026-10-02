// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.MO2.Models;

/// <summary>
/// Строка plugins.txt: имя плагина + флаг "включён".
/// </summary>
public sealed record PluginEntry(string Name, bool Enabled);
