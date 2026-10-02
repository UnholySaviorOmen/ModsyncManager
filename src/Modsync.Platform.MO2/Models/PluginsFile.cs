// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.MO2.Models;

public sealed class PluginsFile
{
    public required IReadOnlyList<PluginEntry> Entries { get; init; }
}
