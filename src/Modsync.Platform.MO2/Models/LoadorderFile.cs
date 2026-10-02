// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.MO2.Models;

public sealed class LoadorderFile
{
    /// <summary>Порядок загрузки: сверху — раньше.</summary>
    public required IReadOnlyList<string> Plugins { get; init; }
}
